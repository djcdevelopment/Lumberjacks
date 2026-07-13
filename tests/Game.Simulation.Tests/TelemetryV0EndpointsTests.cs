using System.Text.Json;
using Game.Contracts.Entities;
using Game.ServiceDefaults;
using Game.Simulation.Endpoints;
using Game.Simulation.World;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Game.Simulation.Tests;

/// <summary>
/// Public Telemetry API v0 (community-telemetry-strategy.md Phase 3, G1) — response-shape
/// coverage for the four endpoints hosted in Game.Simulation.Endpoints, plus the hard privacy
/// rule from the strategy doc: no player ids, names, or positions may EVER appear in a v0
/// response. See tests/Game.Gateway.Tests/TelemetryV0SessionsEndpointsTests.cs for the
/// equivalent coverage of the sessions endpoint (which needs Gateway-only SessionManager state,
/// hence the separate test project).
///
/// Each Build*Info method is a plain static function of its dependencies (no HttpContext), so
/// it's tested directly here the same way the endpoints call it — no host required.
/// </summary>
public class TelemetryV0EndpointsTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private static string ToJson(object response) => JsonSerializer.Serialize(response, JsonOptions);

    // Deliberately large/oddly-precise coordinates — collision-proof against legitimate small
    // integer telemetry values (tick_rate_hz=20, near_radius=100, budget_ms=50, etc.) so a
    // substring match below can only mean the Position genuinely leaked, not a false positive.
    private static readonly Vec3 SentinelPosition = new(918273.645, 827364.591, 736451.827);

    private static WorldState WorldWithPlayers(params (string Id, string Name)[] players)
    {
        var world = new WorldState();
        foreach (var (id, name) in players)
        {
            world.Players[id] = new Player
            {
                Id = id,
                Name = name,
                Position = SentinelPosition,
                RegionId = "region-spawn",
                Connected = true,
                ConnectedAt = DateTimeOffset.UtcNow,
            };
        }
        return world;
    }

    // ── Server info ──

    [Fact]
    public void ServerInfoCarriesEnvelopeAndReplicationBlock()
    {
        var world = new WorldState();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Replication:Policy"] = "radius",
                ["Replication:NearRadius"] = "150",
                ["Replication:MidRadius"] = "400",
                ["Replication:MidTickInterval"] = "3",
                ["Replication:SendWorkers"] = "2",
                ["Replication:BroadcastDeadlineMs"] = "40",
                ["Replication:AdaptiveDegrade"] = "true",
            })
            .Build();

        var json = ToJson(TelemetryV0Endpoints.BuildServerInfo(world, config));

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("v0", root.GetProperty("api_version").GetString());
        Assert.Equal("unstable", root.GetProperty("stability").GetString());
        Assert.True(root.TryGetProperty("current_tick", out _));
        Assert.Equal(20, root.GetProperty("tick_rate_hz").GetInt32());
        Assert.True(root.TryGetProperty("uptime_seconds", out _));
        Assert.True(root.TryGetProperty("started_at", out _));

        var repl = root.GetProperty("replication");
        Assert.Equal("radius", repl.GetProperty("policy").GetString());
        Assert.Equal(150, repl.GetProperty("near_radius").GetDouble());
        Assert.Equal(400, repl.GetProperty("mid_radius").GetDouble());
        Assert.Equal(3, repl.GetProperty("mid_tick_interval").GetInt32());
        Assert.Equal(2, repl.GetProperty("send_workers").GetInt32()); // explicit config passes through as-is
        Assert.Equal(40, repl.GetProperty("deadline_ms").GetInt32());
        Assert.True(repl.GetProperty("adaptive").GetBoolean());
    }

    [Fact]
    public void ServerInfoAutoResolvesSendWorkersWithoutWaitingOnTickMetricsWindow()
    {
        // SendWorkers=0 means "auto" — must resolve immediately (no TickMetrics dependency,
        // unlike /tick's tick_timing which is null until the first ~5s window closes).
        var world = new WorldState();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Replication:SendWorkers"] = "0" })
            .Build();

        var json = ToJson(TelemetryV0Endpoints.BuildServerInfo(world, config));
        using var doc = JsonDocument.Parse(json);
        var sendWorkers = doc.RootElement.GetProperty("replication").GetProperty("send_workers").GetInt32();

        Assert.True(sendWorkers >= 1);
    }

    // ── Tick info ──

    [Fact]
    public void TickInfoGracefullyNullBeforeFirstWindow()
    {
        var json = ToJson(TelemetryV0Endpoints.BuildTickInfo(metrics: null));

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("v0", root.GetProperty("api_version").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("tick_timing").ValueKind);
    }

    // ── Delivery info ──

    [Fact]
    public void DeliveryInfoWrapsDeliveryTransitionAndUdpSnapshots()
    {
        var json = ToJson(TelemetryV0Endpoints.BuildDeliveryInfo());

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("v0", root.GetProperty("api_version").GetString());
        Assert.Equal(JsonValueKind.Object, root.GetProperty("delivery").ValueKind);
        Assert.Equal(JsonValueKind.Object, root.GetProperty("transitions").ValueKind);

        var udp = root.GetProperty("udp_packets");
        Assert.Equal(JsonValueKind.Object, udp.ValueKind);
        // The block always carries the full outcome set + total + derived reject_rate.
        Assert.True(udp.TryGetProperty("received", out _));
        Assert.True(udp.TryGetProperty("invalid", out _));
        Assert.True(udp.TryGetProperty("unknown_session", out _));
        Assert.True(udp.TryGetProperty("send_error", out _));
        Assert.True(udp.TryGetProperty("total", out _));
        Assert.True(udp.TryGetProperty("reject_rate", out _));
    }

    // ── UDP packet-outcome block (D-06): reject/error rate, NOT network loss ──

    [Fact]
    public void UdpPacketsInfoComputesRejectRateOverAllReceivedPackets()
    {
        // received=90, rejected = invalid(4) + unknown_session(5) + send_error(1) = 10; total=100.
        var snapshot = new Dictionary<string, long>
        {
            ["received"] = 90,
            ["invalid"] = 4,
            ["unknown_session"] = 5,
            ["send_error"] = 1,
        };

        var json = ToJson(TelemetryV0Endpoints.BuildUdpPacketsInfo(snapshot));
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(90, root.GetProperty("received").GetInt64());
        Assert.Equal(4, root.GetProperty("invalid").GetInt64());
        Assert.Equal(5, root.GetProperty("unknown_session").GetInt64());
        Assert.Equal(1, root.GetProperty("send_error").GetInt64());
        Assert.Equal(100, root.GetProperty("total").GetInt64());
        Assert.Equal(0.10, root.GetProperty("reject_rate").GetDouble(), 10);
    }

    [Fact]
    public void UdpPacketsInfoMissingOutcomesTreatedAsZero()
    {
        // Only 'received' has ever been recorded — every reject outcome absent from the dict.
        var snapshot = new Dictionary<string, long> { ["received"] = 7 };

        var json = ToJson(TelemetryV0Endpoints.BuildUdpPacketsInfo(snapshot));
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(7, root.GetProperty("received").GetInt64());
        Assert.Equal(0, root.GetProperty("invalid").GetInt64());
        Assert.Equal(0, root.GetProperty("unknown_session").GetInt64());
        Assert.Equal(0, root.GetProperty("send_error").GetInt64());
        Assert.Equal(7, root.GetProperty("total").GetInt64());
        Assert.Equal(0.0, root.GetProperty("reject_rate").GetDouble(), 10);
    }

    [Fact]
    public void UdpPacketsInfoZeroPacketsYieldsZeroRejectRateNotNaN()
    {
        // Empty snapshot (no UDP traffic since startup) must guard divide-by-zero → 0.0, not NaN.
        var json = ToJson(TelemetryV0Endpoints.BuildUdpPacketsInfo(new Dictionary<string, long>()));
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(0, root.GetProperty("total").GetInt64());
        var rejectRate = root.GetProperty("reject_rate").GetDouble();
        Assert.Equal(0.0, rejectRate);
        Assert.False(double.IsNaN(rejectRate));
    }

    [Fact]
    public void RecordUdpPacketFeedsSnapshotTally()
    {
        // Static, process-cumulative tally: assert on the DELTA so the test is robust to any
        // ordering/parallelism (no other unit test records UDP outcomes).
        long Before(string k) => LumberjacksTelemetry.SnapshotUdpPackets().TryGetValue(k, out var v) ? v : 0;

        var beforeReceived = Before("received");
        var beforeInvalid = Before("invalid");
        var beforeUnknown = Before("unknown_session");
        var beforeSendErr = Before("send_error");

        LumberjacksTelemetry.RecordUdpPacket("received");
        LumberjacksTelemetry.RecordUdpPacket("received");
        LumberjacksTelemetry.RecordUdpPacket("invalid");
        LumberjacksTelemetry.RecordUdpPacket("unknown_session");
        LumberjacksTelemetry.RecordUdpPacket("send_error");

        var after = LumberjacksTelemetry.SnapshotUdpPackets();
        Assert.Equal(beforeReceived + 2, after["received"]);
        Assert.Equal(beforeInvalid + 1, after["invalid"]);
        Assert.Equal(beforeUnknown + 1, after["unknown_session"]);
        Assert.Equal(beforeSendErr + 1, after["send_error"]);
    }

    // ── Regions info ──

    [Fact]
    public void RegionsInfoExposesOnlyStaticWorldFacts()
    {
        var world = new WorldState();
        var json = ToJson(TelemetryV0Endpoints.BuildRegionsInfo(world));

        using var doc = JsonDocument.Parse(json);
        var region = doc.RootElement.GetProperty("regions")[0];
        Assert.Equal("region-spawn", region.GetProperty("id").GetString());
        Assert.Equal("Spawn Island", region.GetProperty("name").GetString());
        Assert.True(region.TryGetProperty("player_count", out _));
        Assert.True(region.TryGetProperty("tick_rate", out _));

        var bounds = region.GetProperty("bounds");
        Assert.True(bounds.GetProperty("min").TryGetProperty("x", out _));
        Assert.True(bounds.GetProperty("max").TryGetProperty("x", out _));
    }

    // ── Privacy (hard rule): no player id, name, or position may EVER appear ──

    [Fact]
    public void NoV0ResponseLeaksConnectedPlayerIdentifiersOrPositions()
    {
        const string playerAId = "player-alpha-3f9c1e";
        const string playerAName = "SneakyLumberjackAlpha";
        const string playerBId = "player-bravo-7d2a08";
        const string playerBName = "SneakyLumberjackBravo";

        var world = WorldWithPlayers((playerAId, playerAName), (playerBId, playerBName));
        var config = new ConfigurationBuilder().Build();

        var responses = new object[]
        {
            TelemetryV0Endpoints.BuildServerInfo(world, config),
            TelemetryV0Endpoints.BuildTickInfo(metrics: null),
            TelemetryV0Endpoints.BuildDeliveryInfo(),
            TelemetryV0Endpoints.BuildRegionsInfo(world),
        };

        // Sentinel position coordinates, formatted the same way System.Text.Json would render
        // them, so a substring hit can only mean Position genuinely leaked into the response.
        var sentinelCoords = new[]
        {
            SentinelPosition.X.ToString(System.Globalization.CultureInfo.InvariantCulture),
            SentinelPosition.Y.ToString(System.Globalization.CultureInfo.InvariantCulture),
            SentinelPosition.Z.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };

        foreach (var response in responses)
        {
            var json = ToJson(response);

            Assert.DoesNotContain(playerAId, json);
            Assert.DoesNotContain(playerAName, json);
            Assert.DoesNotContain(playerBId, json);
            Assert.DoesNotContain(playerBName, json);
            foreach (var coord in sentinelCoords)
                Assert.DoesNotContain(coord, json);
        }
    }
}
