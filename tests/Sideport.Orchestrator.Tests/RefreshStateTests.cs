using Sideport.Orchestrator;

namespace Sideport.Orchestrator.Tests;

/// <summary>
/// Coverage for <see cref="RefreshState.IsDue"/> — when the scheduler considers
/// an app due, including the optional fixed re-sign cadence (e.g. daily) that
/// keeps a fresh safety margin well before the 7-day profile expiry.
/// </summary>
public class RefreshStateTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 21, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lead = TimeSpan.FromDays(2);
    private static readonly TimeSpan Daily = TimeSpan.FromDays(1);

    private static RefreshState State(DateTimeOffset? expiresAt, DateTimeOffset? lastSucceeded) =>
        new("UDID", "com.example.app", expiresAt, lastSucceeded, lastSucceeded is not null, null, lastSucceeded);

    [Fact]
    public void IsDue_NeverSigned_IsDue() =>
        Assert.True(State(null, null).IsDue(Now, Lead));

    [Fact]
    public void IsDue_WithinLeadTime_IsDue() =>
        Assert.True(State(Now.AddDays(1), Now.AddDays(-6)).IsDue(Now, Lead));

    [Fact]
    public void IsDue_FarFromExpiry_NoCadence_NotDue() =>
        Assert.False(State(Now.AddDays(6), Now.AddHours(-1)).IsDue(Now, Lead));

    [Fact]
    public void IsDue_DailyCadenceElapsed_IsDue_EvenWhenFarFromExpiry() =>
        // signed 25h ago, not near expiry, daily cadence -> due
        Assert.True(State(Now.AddDays(6), Now.AddHours(-25)).IsDue(Now, Lead, Daily));

    [Fact]
    public void IsDue_DailyCadenceNotElapsed_NotDue() =>
        // signed 2h ago, not near expiry, daily cadence -> not due
        Assert.False(State(Now.AddDays(6), Now.AddHours(-2)).IsDue(Now, Lead, Daily));

    [Fact]
    public void IsDue_CadenceSet_NoRecordedSuccess_IsDue() =>
        Assert.True(new RefreshState("U", "b", Now.AddDays(6), Now.AddHours(-1), true, null, null)
            .IsDue(Now, Lead, Daily));

    [Fact]
    public void IsDue_FailedRecently_BackoffSuppresses() =>
        Assert.False(new RefreshState("U", "b", null, Now.AddMinutes(-5), false, "unreachable", null)
            .IsDue(Now, Lead, null, TimeSpan.FromMinutes(15)));

    [Fact]
    public void IsDue_FailedPastBackoff_RetriesAgain() =>
        Assert.True(new RefreshState("U", "b", null, Now.AddMinutes(-20), false, "unreachable", null)
            .IsDue(Now, Lead, null, TimeSpan.FromMinutes(15)));
}
