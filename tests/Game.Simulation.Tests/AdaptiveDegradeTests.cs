using Game.Simulation.Tick;
using Xunit;

namespace Game.Simulation.Tests;

public class AdaptiveDegradeTests
{
    // ── ShouldDegrade: triggers after overrun, lifts after fit ──

    [Theory]
    [InlineData(false, 200.0, false)]  // disabled — never degrades, even wildly over budget
    [InlineData(true, 0.0, false)]     // enabled, no prior broadcast recorded yet
    [InlineData(true, 49.9, false)]    // enabled, under budget
    [InlineData(true, 50.0, false)]    // enabled, exactly at budget — not an overrun
    [InlineData(true, 50.1, true)]     // enabled, over budget — triggers
    [InlineData(true, 200.0, true)]
    public void ShouldDegradeTriggersOnlyWhenEnabledAndOverBudget(bool enabled, double previousMs, bool expected)
        => Assert.Equal(expected, AdaptiveDegrade.ShouldDegrade(enabled, previousMs));

    [Fact]
    public void LiftsImmediatelyOnceThePreviousBroadcastFits()
    {
        // Stateless beyond "last broadcast ms": an overrun tick followed by a tick that fits
        // must lift degrade on the very next decision — no cooldown, no hysteresis.
        Assert.True(AdaptiveDegrade.ShouldDegrade(enabled: true, previousBroadcastWallMs: 80.0));
        Assert.False(AdaptiveDegrade.ShouldDegrade(enabled: true, previousBroadcastWallMs: 30.0));
    }

    [Fact]
    public void RespectsACustomBudget()
    {
        Assert.False(AdaptiveDegrade.ShouldDegrade(enabled: true, previousBroadcastWallMs: 15.0, budgetMs: 20.0));
        Assert.True(AdaptiveDegrade.ShouldDegrade(enabled: true, previousBroadcastWallMs: 25.0, budgetMs: 20.0));
    }

    // ── ShouldSkipAlternating: halving selection for radius/full ──

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(3, true)]
    [InlineData(400, false)]
    [InlineData(401, true)]
    public void AlternatingSkipsOddRotatedPositions(int rotatedPosition, bool expectedSkip)
        => Assert.Equal(expectedSkip, AdaptiveDegrade.ShouldSkipAlternating(rotatedPosition));

    [Fact]
    public void AlternatingSelectsRoughlyHalfOfAnySequence()
    {
        var skipped = Enumerable.Range(0, 401).Count(AdaptiveDegrade.ShouldSkipAlternating);
        Assert.Equal(200, skipped); // positions 1,3,...,399 — half (rounded down for an odd count)
    }
}
