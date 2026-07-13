using Sideport.Api.Onboarding;
using Sideport.Api.Operations;

namespace Sideport.Api.Tests;

public sealed class OnboardingCompletionStoreTests
{
    [Fact]
    public async Task CreateAsync_PersistsOneImmutableReceipt()
    {
        string path = ReceiptPath();
        var store = new OnboardingCompletionStore(path);
        OnboardingCompletionReceipt first = Receipt("op_first", "catalog-first");

        (OnboardingCompletionReceipt created, bool wasCreated) = await store.CreateAsync(first);
        (OnboardingCompletionReceipt replayed, bool replayCreated) = await store.CreateAsync(
            Receipt("op_second", "catalog-second"));

        Assert.True(wasCreated);
        Assert.Equal(first, created);
        Assert.False(replayCreated);
        Assert.Equal(first, replayed);
        Assert.Equal(first, await new OnboardingCompletionStore(path).ReadAsync());
    }

    [Fact]
    public async Task ReadAsync_CorruptReceiptFailsClosed()
    {
        string path = ReceiptPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "{ not-json");
        var store = new OnboardingCompletionStore(path);

        await Assert.ThrowsAsync<OnboardingCompletionStoreException>(() => store.ReadAsync());
        await Assert.ThrowsAsync<OnboardingCompletionStoreException>(() => store.CreateAsync(Receipt("op_new", "catalog-new")));
        Assert.Equal("{ not-json", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task CreateAsync_RejectsIncompleteEvidence()
    {
        var store = new OnboardingCompletionStore(ReceiptPath());
        OnboardingCompletionReceipt invalid = Receipt("", "catalog-app");

        await Assert.ThrowsAsync<InvalidDataException>(() => store.CreateAsync(invalid));
    }

    private static OnboardingCompletionReceipt Receipt(string operationId, string catalogAppId) =>
        new(
            OnboardingCompletionStore.CurrentSchemaVersion,
            new DateTimeOffset(2026, 7, 11, 12, 0, 0, TimeSpan.Zero),
            new OperationActorDto("api-token", "api-token-client"),
            "acct_test",
            "TEAMID1234",
            "TEST-UDID",
            catalogAppId,
            CatalogVersion: 1,
            CatalogSha256: "sha256",
            "com.example.app",
            operationId,
            SchedulerSettingsVersion: "settings_1",
            OperationalCheckedAt: new DateTimeOffset(2026, 7, 11, 11, 59, 59, TimeSpan.Zero));

    private static string ReceiptPath() => Path.Combine(
        Path.GetTempPath(),
        "sideport-onboarding-receipt-tests",
        Guid.NewGuid().ToString("N"),
        "onboarding-completion.json");
}
