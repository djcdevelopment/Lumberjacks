using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Game.ServiceDefaults;

public static class LumberjacksTelemetry
{
    public const string ActivitySourceName = "Lumberjacks.Gameplay";
    public const string MeterName = "Lumberjacks.Gameplay";

    private static readonly ActivitySource Activities = new(ActivitySourceName);
    private static readonly Meter Metrics = new(MeterName);
    private static readonly Counter<long> Messages = Metrics.CreateCounter<long>(
        "lumberjacks.messages", description: "Gameplay messages processed by transport and type.");
    private static readonly UpDownCounter<long> ActiveSessions = Metrics.CreateUpDownCounter<long>(
        "lumberjacks.sessions.active", description: "Currently attached gameplay sessions.");
    private static readonly Counter<long> SessionTransitions = Metrics.CreateCounter<long>(
        "lumberjacks.sessions.transitions", description: "Gameplay session lifecycle transitions.");
    private static readonly Counter<long> UdpPackets = Metrics.CreateCounter<long>(
        "lumberjacks.udp.packets", unit: "{packet}", description: "UDP packet outcomes.");
    private static readonly Counter<long> Delivery = Metrics.CreateCounter<long>(
        "lumberjacks.delivery", unit: "{message}", description: "Gameplay delivery path outcomes.");
    private static readonly Histogram<double> TickDuration = Metrics.CreateHistogram<double>(
        "lumberjacks.tick.duration", unit: "ms", description: "Authoritative simulation tick duration.");
    private static readonly Counter<long> TickOverruns = Metrics.CreateCounter<long>(
        "lumberjacks.tick.overruns", unit: "{tick}", description: "Simulation ticks exceeding the 50 ms budget.");
    private static readonly Histogram<long> ChangedEntities = Metrics.CreateHistogram<long>(
        "lumberjacks.tick.changed_entities", unit: "{entity}", description: "Entities changed per simulation tick.");

    public static Activity? StartMessageActivity(string messageType, string transport)
    {
        if (messageType is "player_input" or "player_move")
            return null;

        var activity = Activities.StartActivity("game.message", ActivityKind.Server);
        activity?.SetTag("game.message.type", messageType);
        activity?.SetTag("network.transport", transport);
        return activity;
    }

    public static void RecordMessage(string messageType, string transport) =>
        Messages.Add(1,
            new KeyValuePair<string, object?>("message.type", messageType),
            new KeyValuePair<string, object?>("network.transport", transport));

    public static void SessionCreated(bool resumed)
    {
        ActiveSessions.Add(1);
        SessionTransitions.Add(1, new KeyValuePair<string, object?>("transition", resumed ? "resumed" : "created"));
    }

    public static void SessionDetached()
    {
        ActiveSessions.Add(-1);
        SessionTransitions.Add(1, new KeyValuePair<string, object?>("transition", "detached"));
    }

    public static void RecordUdpPacket(string outcome) =>
        UdpPackets.Add(1, new KeyValuePair<string, object?>("outcome", outcome));

    public static void RecordDelivery(string path) =>
        Delivery.Add(1, new KeyValuePair<string, object?>("path", path));

    public static void RecordTick(TimeSpan duration, int changedPlayers, int changedResources)
    {
        TickDuration.Record(duration.TotalMilliseconds);
        ChangedEntities.Record(changedPlayers + changedResources);
        if (duration.TotalMilliseconds > 50)
            TickOverruns.Add(1);
    }
}
