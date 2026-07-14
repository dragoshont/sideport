using Sideport.Api.WorkspaceAccess;

namespace Sideport.Api.Tests;

public sealed class OwnerSetupLinkBootstrapperTests
{
    [Fact]
    public async Task FreshNativeDeployment_EmitsOneFragmentLinkWithoutPersistingPlaintext()
    {
        string directory = StoreDirectory();
        var store = new WorkspaceAccessStore(directory);
        var sink = new RecordingSink();
        var bootstrapper = new OwnerSetupLinkBootstrapper(
            store,
            new Uri("https://sideport.example/"),
            sink);

        await bootstrapper.EnsureAsync();

        Uri setupUrl = Assert.Single(sink.Urls);
        Assert.Equal("https", setupUrl.Scheme);
        Assert.Equal("sideport.example", setupUrl.Host);
        Assert.Equal("/owner-claim", setupUrl.AbsolutePath);
        string token = setupUrl.Fragment[1..];
        Assert.StartsWith("spown1_", token, StringComparison.Ordinal);
        string persisted = await File.ReadAllTextAsync(store.StatePath);
        Assert.DoesNotContain(token, persisted, StringComparison.Ordinal);

        await bootstrapper.EnsureAsync();
        Assert.Single(sink.Urls);
    }

    [Fact]
    public async Task RestartWithPendingClaim_DoesNotReemitOrReplaceThePrivateLink()
    {
        string directory = StoreDirectory();
        var firstSink = new RecordingSink();
        await new OwnerSetupLinkBootstrapper(
            new WorkspaceAccessStore(directory),
            new Uri("https://sideport.example/"),
            firstSink).EnsureAsync();
        Uri firstUrl = Assert.Single(firstSink.Urls);

        var restartedSink = new RecordingSink();
        await new OwnerSetupLinkBootstrapper(
            new WorkspaceAccessStore(directory),
            new Uri("https://sideport.example/"),
            restartedSink).EnsureAsync();

        Assert.Empty(restartedSink.Urls);
        WorkspaceAccessDocument document = (await new WorkspaceAccessStore(directory).ReadAsync())!;
        Assert.Single(document.OwnerClaims);
        Assert.DoesNotContain(firstUrl.Fragment[1..], await File.ReadAllTextAsync(
            Path.Combine(directory, WorkspaceAccessStore.FileName)), StringComparison.Ordinal);
    }

    private static string StoreDirectory() => Path.Combine(
        Path.GetTempPath(),
        "sideport-owner-setup-link-tests",
        Guid.NewGuid().ToString("N"));

    private sealed class RecordingSink : IOwnerSetupLinkSink
    {
        internal List<Uri> Urls { get; } = [];

        public void Write(Uri setupUrl) => Urls.Add(setupUrl);
    }
}
