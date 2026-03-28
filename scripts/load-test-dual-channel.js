#!/usr/bin/env node
/**
 * load-test-dual-channel.js — Heavy-load gut check for dual-channel transport.
 *
 * Spawns N bot players that:
 *   1. Connect via WebSocket (binary protocol)
 *   2. Bind a UDP channel using their session token
 *   3. Send player_input at 20Hz via UDP (binary, 19 bytes per packet)
 *   4. Receive entity_update via both WebSocket and UDP
 *   5. Print a live dashboard every 2 seconds
 *
 * Usage:
 *   node scripts/load-test-dual-channel.js [gateway_url] [player_count] [duration_sec]
 *
 * Examples:
 *   node scripts/load-test-dual-channel.js                          # 20 bots, localhost, 30s
 *   node scripts/load-test-dual-channel.js ws://localhost:4000 50   # 50 bots, 30s
 *   node scripts/load-test-dual-channel.js ws://localhost:4000 100 60
 *
 * What you'll see:
 *   - Live connection count (WS + UDP binds)
 *   - Input packets sent (UDP) vs entity updates received (WS binary + UDP)
 *   - Bytes/sec per channel
 *   - Observed tick rate
 *   - Per-bot latency estimate (input_seq round-trip)
 */

const WebSocket = require("ws");
const dgram = require("dgram");

// ── Config ──────────────────────────────────────────────────────────
const GATEWAY_WS = process.argv[2] || "ws://localhost:4000";
const BOT_COUNT  = parseInt(process.argv[3] || "20", 10);
const DURATION_S = parseInt(process.argv[4] || "30", 10);
const INPUT_HZ   = 20;    // match server tick rate
const REGION_ID  = "region-spawn";

// Derive UDP host/port from WS URL
const wsUrl = new URL(GATEWAY_WS);
const UDP_HOST = wsUrl.hostname;
const UDP_PORT = 4005;

// ── Binary Protocol Helpers ─────────────────────────────────────────

// BitWriter — mirrors the C# BitWriter (big-endian bit order)
class BitWriter {
  constructor(size) {
    this.buf = Buffer.alloc(size);
    this.bitPos = 0;
  }
  writeBits(value, count) {
    for (let i = count - 1; i >= 0; i--) {
      const byteIdx = this.bitPos >> 3;
      const bitIdx = 7 - (this.bitPos & 7);
      if ((value >> i) & 1) {
        this.buf[byteIdx] |= (1 << bitIdx);
      }
      this.bitPos++;
    }
  }
  writeBool(v) { this.writeBits(v ? 1 : 0, 1); }
  writeByte(v) { this.writeBits(v & 0xFF, 8); }
  writeUInt16(v) { this.writeBits(v & 0xFFFF, 16); }
  writeUInt32(v) { this.writeBits(v >>> 0, 32); }
  get byteLength() { return Math.ceil(this.bitPos / 8); }
  get buffer() { return this.buf.subarray(0, this.byteLength); }
}

// BitReader — mirrors the C# BitReader
class BitReader {
  constructor(buf) {
    this.buf = buf;
    this.bitPos = 0;
  }
  readBits(count) {
    let value = 0;
    for (let i = 0; i < count; i++) {
      const byteIdx = this.bitPos >> 3;
      const bitIdx = 7 - (this.bitPos & 7);
      value = (value << 1) | ((this.buf[byteIdx] >> bitIdx) & 1);
      this.bitPos++;
    }
    return value;
  }
  readBool() { return this.readBits(1) === 1; }
  readByte() { return this.readBits(8); }
  readUInt16() { return this.readBits(16); }
  readUInt32() { return this.readBits(32); }
}

// Message type IDs (from MessageTypeId.cs)
const MSG = {
  PlayerInput:  6,
  EntityUpdate: 12,
  EntityRemoved: 13,
};

// Build a binary envelope + PlayerInput payload
function buildPlayerInputPacket(direction, speed, inputSeq) {
  // Payload: direction(1) + speed(1) + actionFlags(1) + inputSeq(2) = 5 bytes
  const payloadSize = 5;

  // Envelope header: 43 bits = 6 bytes
  const w = new BitWriter(6 + payloadSize);
  // Header
  w.writeBits(1, 4);              // version = 1
  w.writeBits(MSG.PlayerInput, 6); // type = PlayerInput (6)
  w.writeBool(true);               // lane = Datagram (1)
  w.writeUInt16(0);                // seq = 0 (datagrams don't need ordering)
  w.writeUInt16(payloadSize);      // payloadLen = 5
  // Payload
  w.writeByte(direction);
  w.writeByte(speed);
  w.writeByte(0);                  // actionFlags = 0
  w.writeUInt16(inputSeq);

  return w.buffer;
}

// Parse a binary envelope header from a buffer
function parseEnvelopeHeader(buf) {
  if (buf.length < 6) return null;
  const r = new BitReader(buf);
  return {
    version:    r.readBits(4),
    type:       r.readBits(6),
    lane:       r.readBool() ? "datagram" : "reliable",
    seq:        r.readUInt16(),
    payloadLen: r.readUInt16(),
  };
}

// Convert a UInt64 decimal string to 8-byte LE buffer
function udpTokenToBytes(tokenStr) {
  const n = BigInt(tokenStr);
  const buf = Buffer.alloc(8);
  buf.writeBigUInt64LE(n);
  return buf;
}

// ── Metrics ─────────────────────────────────────────────────────────
const metrics = {
  wsConnected: 0,
  udpBound: 0,
  sessionStarted: 0,
  worldSnapshots: 0,

  udpInputsSent: 0,
  udpInputBytes: 0,

  wsMessagesRecv: 0,
  wsBinaryRecv: 0,
  wsJsonRecv: 0,
  wsBytesRecv: 0,

  udpRecv: 0,
  udpBytesRecv: 0,

  entityUpdatesWs: 0,
  entityUpdatesUdp: 0,
  entityRemoved: 0,

  errors: 0,
  disconnects: 0,
  lastTick: 0,
  ticksSeen: new Set(),

  // Latency tracking
  latencySamples: [],
  inputSeqSentAt: new Map(), // inputSeq → timestamp
};

// ── Bot Class ───────────────────────────────────────────────────────
class Bot {
  constructor(id) {
    this.id = id;
    this.ws = null;
    this.udpSocket = null;
    this.udpToken = null;
    this.udpTokenBytes = null;
    this.playerId = null;
    this.inputSeq = 0;
    this.inputInterval = null;
    this.direction = Math.floor(Math.random() * 256); // random walk direction
    this.joined = false;
    this.udpBound = false;
  }

  connect() {
    return new Promise((resolve) => {
      const url = `${GATEWAY_WS}?protocol=binary`;
      this.ws = new WebSocket(url);
      this.ws.binaryType = "arraybuffer";

      this.ws.on("open", () => {
        metrics.wsConnected++;
        this._connected = true;
        resolve();
      });

      this.ws.on("message", (data, isBinary) => {
        metrics.wsMessagesRecv++;
        if (isBinary || data instanceof ArrayBuffer || Buffer.isBuffer(data)) {
          this._handleBinary(Buffer.isBuffer(data) ? data : Buffer.from(data));
        } else {
          this._handleJson(typeof data === "string" ? data : data.toString());
        }
      });

      this.ws.on("error", () => { metrics.errors++; });
      this.ws.on("close", () => {
        metrics.disconnects++;
        if (this._connected) metrics.wsConnected--;
        this._connected = false;
        this.stopInput();
      });

      // Timeout if connect takes too long
      setTimeout(() => resolve(), 5000);
    });
  }

  _handleJson(text) {
    metrics.wsJsonRecv++;
    metrics.wsBytesRecv += Buffer.byteLength(text);
    try {
      const msg = JSON.parse(text);
      if (msg.type === "session_started") {
        this.playerId = msg.payload.player_id;
        this.udpToken = msg.payload.udp_token;
        this.udpTokenBytes = udpTokenToBytes(this.udpToken);
        metrics.sessionStarted++;
        // Join region
        this._sendJson("join_region", { region_id: REGION_ID });
      } else if (msg.type === "world_snapshot") {
        metrics.worldSnapshots++;
        this.joined = true;
        // Bind UDP and start sending input
        this._bindUdp();
        this._startInput();
      } else if (msg.type === "entity_update") {
        metrics.entityUpdatesWs++;
        // Check for latency sample
        const seq = msg.payload?._data?.last_input_seq || msg.payload?.last_input_seq;
        this._recordLatency(seq);
      }
    } catch { /* ignore parse errors */ }
  }

  _handleBinary(buf) {
    metrics.wsBinaryRecv++;
    metrics.wsBytesRecv += buf.length;
    const header = parseEnvelopeHeader(buf);
    if (!header) return;

    if (header.type === MSG.EntityUpdate) {
      metrics.entityUpdatesWs++;
      // Try to extract tick from payload for tick tracking
      // Payload starts at byte 6; tick is near the end but varies with entityId length
      // Just count the update — detailed parsing not needed for metrics
    } else if (header.type === MSG.EntityRemoved) {
      metrics.entityRemoved++;
    }
  }

  _bindUdp() {
    if (!this.udpTokenBytes) return;
    this.udpSocket = dgram.createSocket("udp4");

    this.udpSocket.on("message", (msg) => {
      metrics.udpRecv++;
      metrics.udpBytesRecv += msg.length;
      // UDP inbound: [token:8] [envelope:6+] [payload]
      if (msg.length > 14) {
        const envBuf = msg.subarray(8);
        const header = parseEnvelopeHeader(envBuf);
        if (header && header.type === MSG.EntityUpdate) {
          metrics.entityUpdatesUdp++;
        }
      }
    });

    this.udpSocket.on("error", () => { /* ignore */ });

    // Send a bind packet (first UDP packet establishes the session mapping)
    const bindPacket = this._buildUdpInput(0, 0, 0);
    this.udpSocket.send(bindPacket, UDP_PORT, UDP_HOST, (err) => {
      if (!err) {
        metrics.udpBound++;
        this.udpBound = true;
      }
    });
  }

  _buildUdpInput(direction, speed, seq) {
    const envelope = buildPlayerInputPacket(direction, speed, seq);
    // Prepend 8-byte token
    return Buffer.concat([this.udpTokenBytes, envelope]);
  }

  _startInput() {
    // Send player_input at INPUT_HZ via UDP
    this.inputInterval = setInterval(() => {
      if (!this.udpBound || !this.udpSocket) return;

      this.inputSeq = (this.inputSeq + 1) & 0xFFFF;

      // Slowly wander: change direction occasionally
      if (Math.random() < 0.05) {
        this.direction = Math.floor(Math.random() * 256);
      }

      const packet = this._buildUdpInput(this.direction, 80, this.inputSeq);
      this.udpSocket.send(packet, UDP_PORT, UDP_HOST);

      metrics.udpInputsSent++;
      metrics.udpInputBytes += packet.length;

      // Track latency for every 20th input
      if (this.inputSeq % 20 === 0) {
        metrics.inputSeqSentAt.set(`${this.playerId}:${this.inputSeq}`, Date.now());
      }
    }, 1000 / INPUT_HZ);
  }

  _sendJson(type, payload) {
    if (this.ws?.readyState === WebSocket.OPEN) {
      const msg = JSON.stringify({ type, payload });
      this.ws.send(msg);
    }
  }

  _recordLatency(seq) {
    if (!seq || !this.playerId) return;
    const key = `${this.playerId}:${seq}`;
    const sentAt = metrics.inputSeqSentAt.get(key);
    if (sentAt) {
      metrics.latencySamples.push(Date.now() - sentAt);
      metrics.inputSeqSentAt.delete(key);
      // Keep only last 100 samples
      if (metrics.latencySamples.length > 100) {
        metrics.latencySamples = metrics.latencySamples.slice(-100);
      }
    }
  }

  stopInput() {
    if (this.inputInterval) clearInterval(this.inputInterval);
    this.inputInterval = null;
  }

  disconnect() {
    this.stopInput();
    if (this.udpSocket) {
      try { this.udpSocket.close(); } catch {}
    }
    if (this.ws?.readyState === WebSocket.OPEN) {
      this.ws.close();
    }
  }
}

// ── Dashboard ───────────────────────────────────────────────────────
let prevMetrics = {};
let startTime = Date.now();

function snapshotMetrics() {
  return {
    t: Date.now(),
    udpInputsSent: metrics.udpInputsSent,
    udpInputBytes: metrics.udpInputBytes,
    wsBytesRecv: metrics.wsBytesRecv,
    udpBytesRecv: metrics.udpBytesRecv,
    entityUpdatesWs: metrics.entityUpdatesWs,
    entityUpdatesUdp: metrics.entityUpdatesUdp,
    wsMessagesRecv: metrics.wsMessagesRecv,
  };
}

function formatBytes(bytes) {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

function printDashboard() {
  const now = snapshotMetrics();
  const dt = prevMetrics.t ? (now.t - prevMetrics.t) / 1000 : 2;

  const udpInputRate = Math.round((now.udpInputsSent - (prevMetrics.udpInputsSent || 0)) / dt);
  const wsUpdateRate = Math.round((now.entityUpdatesWs - (prevMetrics.entityUpdatesWs || 0)) / dt);
  const udpUpdateRate = Math.round((now.entityUpdatesUdp - (prevMetrics.entityUpdatesUdp || 0)) / dt);
  const wsBytesPerSec = (now.wsBytesRecv - (prevMetrics.wsBytesRecv || 0)) / dt;
  const udpBytesPerSec = (now.udpBytesRecv - (prevMetrics.udpBytesRecv || 0)) / dt;
  const udpOutBytesPerSec = (now.udpInputBytes - (prevMetrics.udpInputBytes || 0)) / dt;

  const elapsed = ((Date.now() - startTime) / 1000).toFixed(0);
  const remaining = DURATION_S - parseInt(elapsed, 10);

  const avgLatency = metrics.latencySamples.length > 0
    ? (metrics.latencySamples.reduce((a, b) => a + b, 0) / metrics.latencySamples.length).toFixed(0)
    : "n/a";

  console.clear();
  console.log("=".repeat(68));
  console.log("  DUAL-CHANNEL LOAD TEST");
  console.log("=".repeat(68));
  console.log(`  Target:  ${GATEWAY_WS}  (UDP → ${UDP_HOST}:${UDP_PORT})`);
  console.log(`  Bots:    ${BOT_COUNT}   Duration: ${DURATION_S}s   Elapsed: ${elapsed}s   Left: ${remaining}s`);
  console.log("-".repeat(68));

  console.log("\n  CONNECTIONS");
  console.log(`    WebSocket connected:  ${metrics.wsConnected}`);
  console.log(`    UDP bound:            ${metrics.udpBound}`);
  console.log(`    Sessions started:     ${metrics.sessionStarted}`);
  console.log(`    World snapshots:      ${metrics.worldSnapshots}`);
  console.log(`    Errors:               ${metrics.errors}`);
  console.log(`    Disconnects:          ${metrics.disconnects}`);

  console.log("\n  OUTBOUND (client → server)");
  console.log(`    UDP player_input:     ${udpInputRate}/s   (total: ${metrics.udpInputsSent})`);
  console.log(`    UDP bandwidth out:    ${formatBytes(udpOutBytesPerSec)}/s`);

  console.log("\n  INBOUND (server → client)");
  console.log(`    WS entity_update:     ${wsUpdateRate}/s   (total: ${metrics.entityUpdatesWs})`);
  console.log(`    UDP entity_update:    ${udpUpdateRate}/s   (total: ${metrics.entityUpdatesUdp})`);
  console.log(`    WS bandwidth in:      ${formatBytes(wsBytesPerSec)}/s`);
  console.log(`    UDP bandwidth in:     ${formatBytes(udpBytesPerSec)}/s`);
  console.log(`    WS messages (total):  ${metrics.wsMessagesRecv}  (binary: ${metrics.wsBinaryRecv}  json: ${metrics.wsJsonRecv})`);

  console.log("\n  LATENCY");
  console.log(`    Avg round-trip:       ${avgLatency} ms   (samples: ${metrics.latencySamples.length})`);

  const totalBwIn = wsBytesPerSec + udpBytesPerSec;
  const udpPct = totalBwIn > 0 ? ((udpBytesPerSec / totalBwIn) * 100).toFixed(1) : "0.0";
  console.log("\n  CHANNEL SPLIT");
  console.log(`    Total inbound:        ${formatBytes(totalBwIn)}/s`);
  console.log(`    UDP share:            ${udpPct}%`);
  console.log(`    WS share:             ${(100 - parseFloat(udpPct)).toFixed(1)}%`);

  console.log("\n" + "=".repeat(68));

  prevMetrics = now;
}

// ── Main ────────────────────────────────────────────────────────────
async function main() {
  console.log(`\nSpawning ${BOT_COUNT} bots against ${GATEWAY_WS}...`);
  console.log(`UDP target: ${UDP_HOST}:${UDP_PORT}`);
  console.log(`Duration: ${DURATION_S} seconds\n`);

  const bots = [];

  // Stagger connections: 5 per 100ms to avoid overwhelming the server
  const BATCH_SIZE = 5;
  const BATCH_DELAY = 100;

  for (let i = 0; i < BOT_COUNT; i += BATCH_SIZE) {
    const batch = [];
    for (let j = i; j < Math.min(i + BATCH_SIZE, BOT_COUNT); j++) {
      const bot = new Bot(j);
      bots.push(bot);
      batch.push(bot.connect());
    }
    await Promise.all(batch);
    if (i + BATCH_SIZE < BOT_COUNT) {
      await new Promise(r => setTimeout(r, BATCH_DELAY));
    }
  }

  console.log(`All ${bots.length} bots connected. Starting load test...\n`);
  startTime = Date.now();
  prevMetrics = snapshotMetrics();

  // Dashboard update interval
  const dashboardInterval = setInterval(printDashboard, 2000);

  // Run for DURATION_S seconds
  await new Promise(r => setTimeout(r, DURATION_S * 1000));

  // Final dashboard
  clearInterval(dashboardInterval);
  printDashboard();

  // Disconnect all bots
  console.log("\nDisconnecting bots...");
  for (const bot of bots) {
    bot.disconnect();
  }

  // Wait a moment for clean disconnect
  await new Promise(r => setTimeout(r, 1000));

  // Final summary
  console.log("\n" + "=".repeat(68));
  console.log("  FINAL SUMMARY");
  console.log("=".repeat(68));
  console.log(`  Total UDP inputs sent:      ${metrics.udpInputsSent}`);
  console.log(`  Total UDP bytes out:        ${formatBytes(metrics.udpInputBytes)}`);
  console.log(`  Total WS entity_updates:    ${metrics.entityUpdatesWs}`);
  console.log(`  Total UDP entity_updates:   ${metrics.entityUpdatesUdp}`);
  console.log(`  Total WS bytes in:          ${formatBytes(metrics.wsBytesRecv)}`);
  console.log(`  Total UDP bytes in:         ${formatBytes(metrics.udpBytesRecv)}`);
  console.log(`  Errors:                     ${metrics.errors}`);
  console.log(`  Disconnects:                ${metrics.disconnects}`);

  const avgLat = metrics.latencySamples.length > 0
    ? (metrics.latencySamples.reduce((a, b) => a + b, 0) / metrics.latencySamples.length).toFixed(0)
    : "n/a";
  console.log(`  Avg latency:                ${avgLat} ms`);

  if (metrics.entityUpdatesUdp > 0) {
    console.log(`\n  ✓ UDP channel is ACTIVE — entity updates flowing via datagrams`);
  } else if (metrics.entityUpdatesWs > 0) {
    console.log(`\n  ⚠ UDP channel NOT receiving — updates coming via WebSocket only`);
    console.log(`    (This is expected if Gateway's UdpTransport isn't bound or sending)`);
  } else {
    console.log(`\n  ✗ No entity updates received on either channel`);
  }

  console.log("=".repeat(68));
  process.exit(0);
}

main().catch(err => {
  console.error("Fatal error:", err);
  process.exit(1);
});
