using System.Diagnostics.Metrics;
using Game.Simulation.Tick;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Game.Simulation.Tests;

public class TickMetricsTests
{
    private sealed class TestMeterFactory : IMeterFactory
    {
        public Meter Create(MeterOptions options) => new(options);
        public void Dispose() { }
    }

    private static TickMetrics CreateMetrics() =>
        new(new TestMeterFactory(), NullLogger<TickMetrics>.Instance);

    private static void RecordUniformTick(TickMetrics metrics, long tick, double totalMs)
        => metrics.RecordTick(tick, totalMs,
            intervalMs: 50, simMs: 1, hashMs: 1, broadcastMs: 1, housekeepingMs: 1);

    [Fact]
    public void NoSnapshotBeforeFirstWindowCloses()
    {
        using var metrics = CreateMetrics();

        for (var t = 1; t < TickMetrics.WindowTicks; t++)
            RecordUniformTick(metrics, t, totalMs: 5);

        Assert.Null(metrics.LastWindow);
    }

    [Fact]
    public void SnapshotAppearsWhenWindowCloses()
    {
        using var metrics = CreateMetrics();

        for (var t = 1; t <= TickMetrics.WindowTicks; t++)
            RecordUniformTick(metrics, t, totalMs: 5);

        var snapshot = metrics.LastWindow;
        Assert.NotNull(snapshot);
        Assert.Equal(TickMetrics.WindowTicks, snapshot.SampleCount);
        Assert.Equal(TickMetrics.WindowTicks, snapshot.WindowEndTick);
        Assert.Equal(TickMetrics.TickBudgetMs, snapshot.BudgetMs);
    }

    [Fact]
    public void PercentilesAndMaxComputedNearestRank()
    {
        using var metrics = CreateMetrics();

        // total durations 1..100 ms → p50 = 50, p99 = 99, max = 100
        for (var t = 1; t <= TickMetrics.WindowTicks; t++)
            RecordUniformTick(metrics, t, totalMs: t);

        var total = metrics.LastWindow!.Phases["total"];
        Assert.Equal(50, total.P50Ms);
        Assert.Equal(99, total.P99Ms);
        Assert.Equal(100, total.MaxMs);
    }

    [Fact]
    public void OverrunsCountTicksOverBudget()
    {
        using var metrics = CreateMetrics();

        // 3 ticks over the 50ms budget, one exactly at budget (not an overrun)
        for (var t = 1; t <= TickMetrics.WindowTicks; t++)
        {
            var totalMs = t switch
            {
                1 => 51.0,
                2 => 80.0,
                3 => 200.0,
                4 => TickMetrics.TickBudgetMs,
                _ => 5.0,
            };
            RecordUniformTick(metrics, t, totalMs);
        }

        Assert.Equal(3, metrics.LastWindow!.Overruns);
    }

    [Fact]
    public void BroadcastPhasesFoldIntoNextTickAndReset()
    {
        using var metrics = CreateMetrics();

        // First tick broadcasts; remaining ticks don't, so interest/send must reset to 0.
        metrics.RecordBroadcastPhases(interestMs: 10, sendMs: 20);
        for (var t = 1; t <= TickMetrics.WindowTicks; t++)
            RecordUniformTick(metrics, t, totalMs: 5);

        var phases = metrics.LastWindow!.Phases;
        Assert.Equal(10, phases["interest"].MaxMs);
        Assert.Equal(20, phases["send"].MaxMs);
        Assert.Equal(0, phases["interest"].P50Ms); // 99 of 100 ticks had no broadcast
        Assert.Equal(0, phases["send"].P50Ms);
    }

    [Fact]
    public void WindowResetsAfterClose()
    {
        using var metrics = CreateMetrics();

        // First window: slow ticks over budget. Second window: fast ticks.
        for (var t = 1; t <= TickMetrics.WindowTicks; t++)
            RecordUniformTick(metrics, t, totalMs: 60);
        for (var t = TickMetrics.WindowTicks + 1; t <= 2 * TickMetrics.WindowTicks; t++)
            RecordUniformTick(metrics, t, totalMs: 2);

        var snapshot = metrics.LastWindow!;
        Assert.Equal(2 * TickMetrics.WindowTicks, snapshot.WindowEndTick);
        Assert.Equal(0, snapshot.Overruns);
        Assert.Equal(2, snapshot.Phases["total"].MaxMs);
    }

    [Fact]
    public void HistogramReceivesPhaseTaggedDurations()
    {
        using var metrics = CreateMetrics();
        var measurements = new List<(double Value, string Phase)>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == TickMetrics.MeterName && instrument.Name == "game.tick.duration")
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, state) =>
        {
            foreach (var tag in tags)
                if (tag.Key == "phase")
                    measurements.Add((value, (string)tag.Value!));
        });
        listener.Start();

        metrics.RecordTick(tick: 1, totalMs: 7, intervalMs: 50,
            simMs: 2, hashMs: 1, broadcastMs: 3, housekeepingMs: 1);

        Assert.Contains((7.0, "total"), measurements);
        Assert.Contains((50.0, "interval"), measurements);
        Assert.Contains((2.0, "sim"), measurements);
        Assert.Contains((3.0, "broadcast"), measurements);
    }
}
