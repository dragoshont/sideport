using Sideport.Api.Operations;

namespace Sideport.Api.Tests;

public sealed class SchedulerSettingsStoreTests
{
    [Fact]
    public async Task InitializeAsync_SafelyRetainsLegacyEnableRequest_AndExistingStateWins()
    {
        string path = StorePath();
        var time = new MutableTimeProvider(DateTimeOffset.Parse("2026-07-12T10:00:00Z"));
        var store = new SchedulerSettingsStore(path, time);

        (SchedulerSettingsState seeded, bool created) = await store.InitializeAsync(
            requestedEnabled: true,
            prerequisitesSatisfied: false);
        time.Set(DateTimeOffset.Parse("2026-07-12T11:00:00Z"));
        (SchedulerSettingsState replayed, bool replayCreated) = await store.InitializeAsync(
            requestedEnabled: true,
            prerequisitesSatisfied: true,
            nextEvaluationAt: DateTimeOffset.Parse("2026-07-12T12:00:00Z"));

        Assert.True(created);
        Assert.False(seeded.Enabled);
        Assert.True(seeded.RequestedEnabled);
        Assert.Equal(1, seeded.SettingsVersion);
        Assert.False(replayCreated);
        Assert.Equal(seeded.Enabled, replayed.Enabled);
        Assert.Equal(seeded.RequestedEnabled, replayed.RequestedEnabled);
        Assert.Equal(seeded.SettingsVersion, replayed.SettingsVersion);
        Assert.Equal(seeded.UpdatedAt, replayed.UpdatedAt);
        SchedulerSettingsState restarted = (await new SchedulerSettingsStore(path).ReadAsync())!;
        Assert.Equal(seeded.SettingsVersion, restarted.SettingsVersion);
        Assert.Equal(seeded.Enabled, restarted.Enabled);
        Assert.Equal(seeded.RequestedEnabled, restarted.RequestedEnabled);
        Assert.Empty(restarted.Evaluations);
    }

    [Fact]
    public async Task SettingsChanges_AreVersionedIdempotentAndDurable()
    {
        string path = StorePath();
        var time = new MutableTimeProvider(DateTimeOffset.Parse("2026-07-12T10:00:00Z"));
        var store = new SchedulerSettingsStore(path, time);
        await store.InitializeAsync(requestedEnabled: false, prerequisitesSatisfied: false);

        time.Set(DateTimeOffset.Parse("2026-07-12T10:01:00Z"));
        (SchedulerSettingsState enabled, bool changed) = await store.SetEnabledAsync(true);
        time.Set(DateTimeOffset.Parse("2026-07-12T10:02:00Z"));
        (SchedulerSettingsState replayed, bool replayChanged) = await store.SetEnabledAsync(true);
        DateTimeOffset next = DateTimeOffset.Parse("2026-07-12T11:01:00Z");
        SchedulerSettingsState scheduled = await store.SetNextEvaluationAtAsync(next);

        Assert.True(changed);
        Assert.Equal(2, enabled.SettingsVersion);
        Assert.False(replayChanged);
        Assert.Equal(enabled.SettingsVersion, replayed.SettingsVersion);
        Assert.Equal(enabled.Enabled, replayed.Enabled);
        Assert.Equal(enabled.UpdatedAt, replayed.UpdatedAt);
        Assert.Equal(2, scheduled.SettingsVersion);
        Assert.Equal(next, scheduled.NextEvaluationAt);
        SchedulerSettingsState restarted = (await new SchedulerSettingsStore(path).ReadAsync())!;
        Assert.Equal(scheduled.SettingsVersion, restarted.SettingsVersion);
        Assert.Equal(scheduled.Enabled, restarted.Enabled);
        Assert.Equal(scheduled.NextEvaluationAt, restarted.NextEvaluationAt);
        Assert.Empty(restarted.Evaluations);

        time.Set(DateTimeOffset.Parse("2026-07-12T10:03:00Z"));
        (SchedulerSettingsState disabled, bool disabledChanged) = await store.SetEnabledAsync(false);
        Assert.True(disabledChanged);
        Assert.Equal(3, disabled.SettingsVersion);
        Assert.Null(disabled.NextEvaluationAt);
    }

    [Fact]
    public async Task RecordEvaluationAsync_RetainsNewestHundredAcrossRestart()
    {
        string path = StorePath();
        DateTimeOffset origin = DateTimeOffset.Parse("2026-07-12T10:00:00Z");
        var store = new SchedulerSettingsStore(path);
        await store.InitializeAsync(requestedEnabled: true, prerequisitesSatisfied: true);

        for (int index = 0; index < SchedulerSettingsStore.MaxEvaluations + 5; index++)
        {
            DateTimeOffset started = origin.AddHours(index);
            await store.RecordEvaluationAsync(
                new SchedulerEvaluationReceipt(
                    $"sched_{index:000}",
                    started,
                    started.AddSeconds(1),
                    "succeeded",
                    DueCount: 0,
                    QueuedCount: 0,
                    BlockedCount: 0,
                    SkippedCount: 0),
                started.AddHours(1));
        }

        SchedulerSettingsState restarted = (await new SchedulerSettingsStore(path).ReadAsync())!;
        Assert.Equal(SchedulerSettingsStore.MaxEvaluations, restarted.Evaluations.Count);
        Assert.Equal("sched_104", restarted.LastEvaluation?.EvaluationId);
        Assert.Equal("sched_005", restarted.Evaluations[^1].EvaluationId);
        Assert.Equal(origin.AddHours(105), restarted.NextEvaluationAt);
        Assert.Equal(1, restarted.SettingsVersion);
    }

    [Fact]
    public async Task CorruptRecord_FailsClosedAndIsNotOverwritten()
    {
        string path = StorePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "{ not-json");
        var store = new SchedulerSettingsStore(path);

        await Assert.ThrowsAsync<SchedulerSettingsStoreException>(() => store.ReadAsync());
        await Assert.ThrowsAsync<SchedulerSettingsStoreException>(() =>
            store.InitializeAsync(requestedEnabled: false, prerequisitesSatisfied: false));
        Assert.Equal("{ not-json", await File.ReadAllTextAsync(path));
    }

    private static string StorePath() => Path.Combine(
        Path.GetTempPath(),
        "sideport-scheduler-settings-tests",
        Guid.NewGuid().ToString("N"),
        "scheduler.json");

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Set(DateTimeOffset utcNow) => _utcNow = utcNow;
    }
}
