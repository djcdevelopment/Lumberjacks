# ADR 0003: WebSocket as Initial Transport

## Status

Accepted

## Context

The platform needs a real-time bidirectional transport between game clients and the gateway service. ADR 0001 established that the client is a thin shell and the server owns truth, which means the transport must support:

- low-latency bidirectional messaging for movement, placement, and world updates
- a structured envelope format (defined in `packages/protocol`)
- compatibility with multiple client engines (Unity, Godot, or future replacements)
- browser support for the admin-web client and future web-based tools

The platform's interest management and activation tier systems will generate variable message rates per region, so the transport must handle bursty traffic without requiring full protocol redesigns later.

## Decision

Use WebSocket (RFC 6455) over TCP as the initial client-server transport.

The protocol layer (`packages/protocol`) wraps all messages in versioned envelopes with JSON serialization. The envelope format is transport-agnostic — switching serialization (to MessagePack, FlatBuffers, or Protobuf) or transport (to WebTransport, QUIC, or raw UDP) requires changes only at the serialization and connection layers, not at the message contract level.

## Consequences

Positive:
- works in every game engine (Unity, Godot, Unreal all have WebSocket support)
- works in browsers (admin-web, debug tools, bot harnesses)
- simple to implement and debug in early development
- TLS support via standard wss:// for production
- compatible with standard load balancers and reverse proxies

Negative:
- TCP head-of-line blocking under packet loss (matters for movement updates at high density)
- no native unreliable channel (every message is ordered and guaranteed)
- higher overhead than raw UDP or custom binary protocols

## Upgrade Path

When the platform reaches density thresholds where TCP head-of-line blocking measurably degrades the player experience (likely the 40+ player spawn island scenario), the transport can be upgraded:

1. **WebTransport** — HTTP/3-based, supports unreliable datagrams, works in browsers
2. **ENet or custom UDP** — traditional game networking for native clients only
3. **Hybrid** — WebSocket for reliable channels (chat, inventory, progression), UDP for unreliable channels (movement, interpolation hints)

The envelope format in `packages/protocol` is designed to survive this transition. A new ADR should be written before any transport change.

## Alternatives Considered

- **WebTransport**: Better theoretical fit for game traffic but ecosystem support is still maturing. Unity and Godot support is experimental. Revisit when v0 density testing reveals TCP as a bottleneck.
- **Raw TCP with custom framing**: More control but loses browser compatibility and adds protocol maintenance burden with no clear upside at this scale.
- **gRPC streaming**: Good for service-to-service but adds complexity and tooling weight for client connections with no benefit over WebSocket at this stage.

## Follow-Up Work

- Define message compression strategy for high-density regions
- Benchmark WebSocket throughput under simulated 40-player spawn island load
- Evaluate WebTransport readiness when client engine is selected
