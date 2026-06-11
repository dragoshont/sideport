namespace Sideport.Orchestrator.Tests;

public class AppleKeychainCredentialProviderTests
{
    [Fact]
    public void Constructor_rejects_empty_service_name()
    {
        Assert.Throws<ArgumentException>(() =>
            new AppleKeychainCredentialProvider(new KeychainCredentialOptions(ServiceName: "")));
    }

    [Fact]
    public void Constructor_throws_clearly_on_non_macos()
    {
        // The guard only fires off macOS. On a macOS dev box this path is not
        // exercised here (the provider constructs fine), so the assertion is
        // scoped to non-macOS hosts (e.g. Linux CI).
        if (OperatingSystem.IsMacOS())
            return;

        var ex = Assert.Throws<PlatformNotSupportedException>(() =>
            new AppleKeychainCredentialProvider(new KeychainCredentialOptions()));
        Assert.Contains("macOS-only", ex.Message);
    }

    [Fact]
    public async Task Missing_item_returns_null_on_macos()
    {
        // macOS-only behavioural check: a service/account that does not exist
        // must resolve to null (no credential), matching the env provider.
        if (!OperatingSystem.IsMacOS())
            return;

        var provider = new AppleKeychainCredentialProvider(
            new KeychainCredentialOptions(ServiceName: "sideport-test-does-not-exist-" + Guid.NewGuid().ToString("N")));
        string? password = await provider.GetPasswordAsync("nobody@example.invalid");
        Assert.Null(password);
    }
}
