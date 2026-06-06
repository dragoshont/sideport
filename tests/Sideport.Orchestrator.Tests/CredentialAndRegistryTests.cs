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
