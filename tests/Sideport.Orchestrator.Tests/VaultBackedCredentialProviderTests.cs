using System.Net;
using System.Text;

namespace Sideport.Orchestrator.Tests;

public class VaultBackedCredentialProviderTests
{
    /// <summary>Captures the last request and returns a canned response.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;
        public HttpRequestMessage? LastRequest { get; private set; }

        public StubHandler(string body, HttpStatusCode status = HttpStatusCode.OK)
        {
            _body = body;
            _status = status;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json"),
            });
        }
    }

    private static string ListResponse(string name, string username, string password) =>
        $$"""
        { "success": true, "data": { "object": "list", "data": [
            { "object": "item", "id": "abc", "name": {{System.Text.Json.JsonSerializer.Serialize(name)}},
              "type": 1, "login": { "username": {{System.Text.Json.JsonSerializer.Serialize(username)}},
              "password": {{System.Text.Json.JsonSerializer.Serialize(password)}}, "totp": null } }
        ] } }
        """;

    private static (VaultBackedCredentialProvider provider, StubHandler handler) Build(
        string body, HttpStatusCode status = HttpStatusCode.OK, VaultCredentialOptions? options = null)
    {
        var handler = new StubHandler(body, status);
        var http = new HttpClient(handler);
        var provider = new VaultBackedCredentialProvider(
            http, options ?? new VaultCredentialOptions("http://vault.test:8087"));
        return (provider, handler);
    }

    [Fact]
    public async Task ReturnsPassword_WhenItemNameMatchesAppleId()
    {
        var (provider, handler) = Build(ListResponse("me@example.com", "me@example.com", "hunter2"));

        string? pw = await provider.GetPasswordAsync("me@example.com");

        Assert.Equal("hunter2", pw);
        // search term is the templated item name, url-encoded
        Assert.Contains("/list/object/items?search=", handler.LastRequest!.RequestUri!.ToString());
        Assert.Contains("me%40example.com", handler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task ReturnsPassword_WhenLoginUsernameMatches_EvenIfNameDiffers()
    {
        // item named "Apple - personal" but login.username is the Apple ID
        var (provider, _) = Build(ListResponse("Apple - personal", "me@example.com", "topsecret"));

        string? pw = await provider.GetPasswordAsync("me@example.com");

        Assert.Equal("topsecret", pw);
    }

    [Fact]
    public async Task MatchIsCaseInsensitive()
    {
        var (provider, _) = Build(ListResponse("Me@Example.COM", "Me@Example.COM", "pw"));

        Assert.Equal("pw", await provider.GetPasswordAsync("me@example.com"));
    }

    [Fact]
    public async Task ReturnsNull_WhenNoItemMatches()
    {
        const string empty = """{ "success": true, "data": { "object": "list", "data": [] } }""";
        var (provider, _) = Build(empty);

        Assert.Null(await provider.GetPasswordAsync("me@example.com"));
    }

    [Fact]
    public async Task AppliesItemNameTemplate()
    {
        var opts = new VaultCredentialOptions("http://vault.test:8087", ItemNameTemplate: "sideport/apple/{appleId}");
        var (provider, handler) = Build(ListResponse("sideport/apple/me@example.com", "me@example.com", "pw"), options: opts);

        string? pw = await provider.GetPasswordAsync("me@example.com");

        Assert.Equal("pw", pw);
        Assert.Contains(Uri.EscapeDataString("sideport/apple/me@example.com"), handler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task SendsBearer_WhenApiKeyConfigured()
    {
        var opts = new VaultCredentialOptions("http://vault.test:8087", ApiKey: "shared-secret");
        var (provider, handler) = Build(ListResponse("me@example.com", "me@example.com", "pw"), options: opts);

        await provider.GetPasswordAsync("me@example.com");

        Assert.True(handler.LastRequest!.Headers.TryGetValues("Authorization", out var values));
        Assert.Equal("Bearer shared-secret", Assert.Single(values!));
    }

    [Fact]
    public async Task Throws_OnVaultError_NotMaskedAsMissing()
    {
        // A 500 from the vault must NOT look like "no credential" — it must throw.
        var (provider, _) = Build("upstream boom", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetPasswordAsync("me@example.com"));
    }

    [Fact]
    public void ExtractPassword_Throws_WhenSuccessFalse()
    {
        const string body = """{ "success": false, "message": "vault is locked" }""";
        Assert.Throws<InvalidOperationException>(() =>
            VaultBackedCredentialProvider.ExtractPassword(body, "me@example.com", "me@example.com"));
    }
}
