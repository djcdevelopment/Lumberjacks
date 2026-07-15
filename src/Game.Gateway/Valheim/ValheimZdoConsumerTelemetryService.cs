using System.Collections.Concurrent;

namespace Game.Gateway.Valheim;

public sealed record ValheimZdoConsumerHeartbeat
{
    public string? WindowId { get; init; }
    public string? ConsumerId { get; init; }
    public string? ModVersion { get; init; }
    public string? TimestampUtc { get; init; }
    public long Applied { get; init; }
    public long Superseded { get; init; }
    public long Acknowledged { get; init; }
    public long Rejected { get; init; }
    public long Duplicates { get; init; }
    public long Retried { get; init; }
    public long Pending { get; init; }
}

public sealed record ValheimZdoConsumerWindowStatus(
    string WindowId,
    int ActiveConsumers,
    long Applied,
    long Superseded,
    long Acknowledged,
    long Rejected,
    long Duplicates,
    long Retried,
    long Pending,
    DateTimeOffset? LastSeen);

/// <summary>Latest aggregate, identity-free telemetry from enrolled ZDO consumers.</summary>
public sealed class ValheimZdoConsumerTelemetryService
{
    private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(15);
    private readonly ConcurrentDictionary<string, ConsumerSample> _samples =
        new(StringComparer.Ordinal);

    public void Record(ValheimZdoConsumerHeartbeat heartbeat)
    {
        var key = heartbeat.WindowId + "@" + heartbeat.ConsumerId;
        _samples[key] = new(heartbeat, DateTimeOffset.UtcNow);
    }

    public ValheimZdoConsumerWindowStatus GetWindowStatus(string windowId)
    {
        var now = DateTimeOffset.UtcNow;
        var active = _samples.Values
            .Where(sample => sample.Heartbeat.WindowId == windowId && now - sample.SeenAt <= StaleAfter)
            .ToList();

        return new(
            windowId,
            active.Count,
            active.Sum(sample => sample.Heartbeat.Applied),
            active.Sum(sample => sample.Heartbeat.Superseded),
            active.Sum(sample => sample.Heartbeat.Acknowledged),
            active.Sum(sample => sample.Heartbeat.Rejected),
            active.Sum(sample => sample.Heartbeat.Duplicates),
            active.Sum(sample => sample.Heartbeat.Retried),
            active.Sum(sample => sample.Heartbeat.Pending),
            active.Count == 0 ? null : active.Max(sample => sample.SeenAt));
    }

    public object Snapshot(string windowId)
    {
        var status = GetWindowStatus(windowId);
        return new
        {
            stability = "unstable",
            window_id = windowId,
            stale = status.ActiveConsumers == 0,
            active_consumers = status.ActiveConsumers,
            applied = status.Applied,
            superseded = status.Superseded,
            acknowledged = status.Acknowledged,
            rejected = status.Rejected,
            duplicates = status.Duplicates,
            retried = status.Retried,
            pending = status.Pending,
            last_seen = status.LastSeen,
        };
    }

    private sealed record ConsumerSample(ValheimZdoConsumerHeartbeat Heartbeat, DateTimeOffset SeenAt);
}
