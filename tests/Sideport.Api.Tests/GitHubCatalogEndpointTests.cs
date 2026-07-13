using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Sideport.Api.Catalog;
using Sideport.Api.GitHubCatalog;

namespace Sideport.Api.Tests;

public sealed class GitHubCatalogEndpointTests
{
    [Fact]
    public async Task Sources_OpenMode_DoesNotDisclosePrivateRepositories()
    {
        var github = new FakeGitHubCatalogService();
        using var factory = Factory(github, apiToken: null);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/v2/catalog/github/sources");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("authentication-required", (await response.Content.ReadFromJsonAsync<ErrorDto>())!.error);
        Assert.Equal(0, github.ListSourcesCalls);
    }

    [Fact]
    public async Task Sources_Authenticated_ReturnsOnlyRedactedDtos()
    {
        var github = new FakeGitHubCatalogService
        {
            Sources = new GitHubSourcesDto(
                new GitHubProviderCapabilityDto(
                    "github-release", true, true, null, new("read", "read")),
                [new("ghsrc_private", "owner/private-app", "private", "github-app", false, new("read", "read"))]),
        };
        using var factory = Factory(github);
        using HttpClient client = AuthenticatedClient(factory);

        HttpResponseMessage response = await client.GetAsync("/api/v2/catalog/github/sources");
        string json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("owner/private-app", json, StringComparison.Ordinal);
        Assert.DoesNotContain("ipaPath", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("privateKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", json, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("public", HttpStatusCode.Created)]
    [InlineData("private", HttpStatusCode.Accepted)]
    public async Task Connect_UsesExpectedCreationStatus(string visibility, HttpStatusCode expected)
    {
        var github = new FakeGitHubCatalogService();
        using var factory = Factory(github);
        using HttpClient client = AuthenticatedClient(factory);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v2/catalog/github/connections",
            new GitHubConnectionRequest("owner/app", visibility, $"connect-{visibility}"));

        Assert.Equal(expected, response.StatusCode);
        GitHubConnectionDto? connection = await response.Content.ReadFromJsonAsync<GitHubConnectionDto>();
        Assert.NotNull(connection);
        Assert.Equal(visibility == "private" ? "authorization-required" : "connected", connection!.Status);
        Assert.Equal("recovery-bearer", github.LastActor);
    }

    [Fact]
    public async Task Import_ReturnsPathFreeCatalogDtoAndStableActor()
    {
        var github = new FakeGitHubCatalogService();
        var imports = new FakeGitHubImportService();
        using var factory = Factory(github, imports);
        using HttpClient client = AuthenticatedClient(factory);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v2/catalog/apps/import-github",
            new GitHubCatalogImportRequest("ghsrc_public", 12, 34, "import-1"));
        string json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("recovery-bearer", imports.LastActor);
        Assert.DoesNotContain("ipaPath", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Path.GetTempPath(), json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SetupCallback_BypassesOidcChallenge_AndUsesFixed303Redirect()
    {
        var github = new FakeGitHubCatalogService();
        using var factory = Factory(github, oidcEnabled: true);
        using HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        HttpResponseMessage response = await client.GetAsync(
            "/github/setup/callback?state=opaque-state&installation_id=42&setup_action=install");

        Assert.Equal(HttpStatusCode.SeeOther, response.StatusCode);
        Assert.Equal(
            "http://127.0.0.1:8080/settings/apps?githubConnection=ghcon_test&source=ghsrc_test",
            response.Headers.Location!.AbsoluteUri);
        Assert.Equal("opaque-state", github.CallbackState);
        Assert.Equal(42, github.CallbackInstallationId);
    }

    [Fact]
    public async Task SetupCallback_FailureNeverReflectsHostileStateOrChallengesOidc()
    {
        var github = new FakeGitHubCatalogService
        {
            CallbackError = new GitHubCatalogException(
                "github-state-invalid",
                "upstream https://evil.example/?token=sentinel"),
        };
        using var factory = Factory(github, oidcEnabled: true);
        using HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        HttpResponseMessage response = await client.GetAsync(
            "/github/setup/callback?state=https%3A%2F%2Fevil.example%2F%3Ftoken%3Dsentinel&installation_id=42&setup_action=install");

        Assert.Equal(HttpStatusCode.SeeOther, response.StatusCode);
        Assert.Equal("http://127.0.0.1:8080/settings/apps?github=failed", response.Headers.Location!.AbsoluteUri);
        Assert.DoesNotContain("evil.example", response.Headers.Location.AbsoluteUri, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sentinel", response.Headers.Location.AbsoluteUri, StringComparison.OrdinalIgnoreCase);
    }

    private static WebApplicationFactory<Program> Factory(
        FakeGitHubCatalogService github,
        FakeGitHubImportService? imports = null,
        string? apiToken = "github-test-token",
        bool oidcEnabled = false)
    {
        string state = Path.Combine(Path.GetTempPath(), "sideport-github-endpoints", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(state);
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Sideport:Apple:DeviceId", "TEST-DEVICE-UUID");
            builder.UseSetting("Sideport:Scheduler:Enabled", "false");
            builder.UseSetting("Sideport:Signer:BinaryPath", typeof(GitHubCatalogEndpointTests).Assembly.Location);
            builder.UseSetting("Sideport:State:Directory", state);
            builder.UseSetting("Sideport:PublicOrigin", "http://127.0.0.1:8080/");
            if (apiToken is not null)
                builder.UseSetting("Sideport:Api:AuthToken", apiToken);
            if (oidcEnabled)
            {
                builder.UseSetting("Sideport:Oidc:Enabled", "true");
                builder.UseSetting("Sideport:Oidc:Authority", "https://identity.example.test");
                builder.UseSetting("Sideport:Oidc:ClientId", "sideport-tests");
                builder.UseSetting("Sideport:Oidc:ClientSecret", "test-only-secret");
            }
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.RemoveAll<IGitHubCatalogService>();
                services.RemoveAll<IGitHubCatalogImportService>();
                services.AddSingleton<IGitHubCatalogService>(github);
                services.AddSingleton<IGitHubCatalogImportService>(imports ?? new FakeGitHubImportService());
            });
        });
    }

    private static HttpClient AuthenticatedClient(WebApplicationFactory<Program> factory)
    {
        HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "github-test-token");
        return client;
    }

    private sealed record ErrorDto(string error);

    private sealed class FakeGitHubCatalogService : IGitHubCatalogService
    {
        public GitHubSourcesDto Sources { get; set; } = new(
            new("github-release", true, true, null, new("read", "read")),
            []);
        public int ListSourcesCalls { get; private set; }
        public string? LastActor { get; private set; }
        public string? CallbackState { get; private set; }
        public long? CallbackInstallationId { get; private set; }
        public GitHubCatalogException? CallbackError { get; set; }

        public Task<GitHubSourcesDto> ListSourcesAsync(CancellationToken ct = default)
        {
            ListSourcesCalls++;
            return Task.FromResult(Sources);
        }

        public Task<GitHubConnectionResult> ConnectAsync(
            GitHubConnectionRequest request,
            string actor,
            CancellationToken ct = default)
        {
            LastActor = actor;
            bool privateRepository = request.Visibility == "private";
            var connection = new GitHubConnectionDto(
                "ghcon_test",
                request.Repository,
                request.Visibility,
                privateRepository ? "authorization-required" : "connected",
                new("read", "read"),
                privateRepository ? DateTimeOffset.UtcNow.AddMinutes(5) : null,
                privateRepository ? null : "ghsrc_test",
                privateRepository
                    ? "https://github.com/apps/sideport-tests/installations/new?state=opaque"
                    : null,
                null);
            return Task.FromResult(new GitHubConnectionResult(connection, Created: true));
        }

        public Task<GitHubConnectionDto?> GetConnectionAsync(
            string connectionId,
            string actor,
            bool owner = false,
            CancellationToken ct = default) => Task.FromResult<GitHubConnectionDto?>(null);

        public Task<GitHubSetupCallbackResult> CompleteInstallationAsync(
            string state,
            long installationId,
            string setupAction,
            CancellationToken ct = default)
        {
            CallbackState = state;
            CallbackInstallationId = installationId;
            if (CallbackError is not null)
                throw CallbackError;
            return Task.FromResult(new GitHubSetupCallbackResult(
                "ghcon_test",
                "ghsrc_test",
                new Uri("http://127.0.0.1:8080/settings/apps?githubConnection=ghcon_test&source=ghsrc_test")));
        }

        public Task<GitHubReleasePageDto> ListReleasesAsync(
            string sourceId,
            int page = 1,
            CancellationToken ct = default) =>
            Task.FromResult(new GitHubReleasePageDto(sourceId, "owner/app", page, []));

        public Task<long?> GetKnownRepositoryIdAsync(
            string sourceId,
            CancellationToken ct = default) => Task.FromResult<long?>(123);

        public Task<GitHubPreparedImport> PrepareImportAsync(
            GitHubCatalogImportRequest request,
            CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeGitHubImportService : IGitHubCatalogImportService
    {
        public string? LastActor { get; private set; }

        public Task<CatalogV2MutationResult> ImportAsync(
            GitHubCatalogImportRequest request,
            string actor,
            CancellationToken ct = default)
        {
            LastActor = actor;
            var entry = new CatalogAppV2Dto(
                "sample", 1, "Sample", "GitHub release app", "com.example.sample",
                "1", "1.0", "live", "ready", 123, new string('a', 64), false, null,
                [new CatalogArtifactSourceDto("github-release", "GitHub release", "owner/app", "v1", "Sample.ipa")],
                DateTimeOffset.UtcNow,
                []);
            return Task.FromResult(new CatalogV2MutationResult(entry, Created: true, Replayed: false));
        }
    }
}
