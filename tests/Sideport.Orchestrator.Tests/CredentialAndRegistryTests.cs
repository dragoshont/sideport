namespace Sideport.Orchestrator.Tests;

public class CredentialAndRegistryTests
{
    [Theory]
    [InlineData("me@example.com", "ME_EXAMPLE_COM")]
    [InlineData("First.Last@icloud.com", "FIRST_LAST_ICLOUD_COM")]
    [InlineData("abc123", "ABC123")]
    public void EnvironmentCredentialProvider_Sanitize_UppercasesAndReplaces(string appleId, string expected)
    {
        Assert.Equal(expected, EnvironmentCredentialProvider.Sanitize(appleId));
    }

    [Fact]
    public async Task EnvironmentCredentialProvider_ReadsFromEnvVar()
    {
        const string var = "SIDEPORT_APPLE_PW_TEST_EXAMPLE_COM";
        Environment.SetEnvironmentVariable(var, "secret-pw");
        try
        {
            var provider = new EnvironmentCredentialProvider();
            string? pw = await provider.GetPasswordAsync("test@example.com");
            Assert.Equal("secret-pw", pw);
        }
        finally
        {
            Environment.SetEnvironmentVariable(var, null);
        }
    }

    [Fact]
    public async Task EnvironmentCredentialProvider_MissingVar_ReturnsNull()
    {
        var provider = new EnvironmentCredentialProvider();
        Assert.Null(await provider.GetPasswordAsync("nobody-" + Guid.NewGuid().ToString("N") + "@x.com"));
    }

    [Fact]
    public async Task InMemoryAppRegistry_UpsertFindRemove()
    {
        var registry = new InMemoryAppRegistry();
        var app = new AppRegistration("com.example.app", "me@example.com", "TEAM", "UDID-1", "/in.ipa");

        await registry.UpsertAsync(app);
        Assert.Equal(app, await registry.FindAsync("UDID-1", "com.example.app"));
        Assert.Single(await registry.ListAsync());

        Assert.True(await registry.RemoveAsync("UDID-1", "com.example.app"));
        Assert.Null(await registry.FindAsync("UDID-1", "com.example.app"));
        Assert.False(await registry.RemoveAsync("UDID-1", "com.example.app"));
    }

    [Fact]
    public async Task FileAppRegistry_LegacyRecordWithoutLifecycle_RemainsActive()
    {
        string directory = Path.Combine(Path.GetTempPath(), "sideport-registry-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "apps.json");
        await File.WriteAllTextAsync(path, """
            [
              {
                "bundleId": "com.example.legacy",
                "appleId": "me@example.com",
                "teamId": "TEAM",
                "deviceUdid": "UDID-1",
                "inputIpaPath": "/legacy.ipa"
              }
            ]
            """);

        var registry = new FileAppRegistry(path);
        AppRegistration app = Assert.Single(await registry.ListAsync());

        Assert.Equal("active", app.Lifecycle);
        Assert.False(app.IsPendingInstall);
    }

    [Fact]
    public async Task FileAppRegistry_RebindAppleAuthority_UpdatesOnlyExactLineageAndPersists()
    {
        string directory = Path.Combine(Path.GetTempPath(), "sideport-registry-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "apps.json");
        var registry = new FileAppRegistry(path);
        await registry.UpsertAsync(new AppRegistration("app.one", "old@example.com", "TEAM1", "UDID-1", "/one.ipa"));
        await registry.UpsertAsync(new AppRegistration("app.two", "old@example.com", "TEAM2", "UDID-2", "/two.ipa"));

        int changed = await registry.RebindAppleAuthorityAsync("old@example.com", "TEAM1", "new@example.com", "TEAM3");

        Assert.Equal(1, changed);
        var restarted = new FileAppRegistry(path);
        AppRegistration updated = (await restarted.FindAsync("UDID-1", "app.one"))!;
        Assert.Equal("new@example.com", updated.AppleId);
        Assert.Equal("TEAM3", updated.TeamId);
        Assert.Equal("TEAM2", (await restarted.FindAsync("UDID-2", "app.two"))!.TeamId);
    }

    [Fact]
    public void RefreshState_IsDue_Logic()
    {
        var now = DateTimeOffset.UtcNow;
        var lead = TimeSpan.FromDays(2);

        var neverSigned = new RefreshState("u", "b", null, null, false, null);
        Assert.True(neverSigned.IsDue(now, lead));

        var farOut = new RefreshState("u", "b", now.AddDays(5), now, true, null);
        Assert.False(farOut.IsDue(now, lead));

        var nearExpiry = new RefreshState("u", "b", now.AddHours(12), now, true, null);
        Assert.True(nearExpiry.IsDue(now, lead));
    }
}
