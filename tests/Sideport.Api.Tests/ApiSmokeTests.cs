using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sideport.Core;

namespace Sideport.Api.Tests;

/// <summary>
/// Integration tests for the HTTP surface: liveness/readiness probes and the
/// bearer-token guard on <c>/api/*</c>. Driven through WebApplicationFactory so
/// the real middleware pipeline + endpoints run, with anisette stubbed (the
/// container sidecar isn't present in CI).
/// </summary>
public class ApiSmokeTests
{
    private static WebApplicationFactory<Program> Factory(
        string? apiToken = null,
        bool anisetteHealthy = true,
        string? signerPath = null)
    {
        signerPath ??= typeof(ApiSmokeTests).Assembly.Location; // a file that exists

        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Sideport:Apple:DeviceId", "TEST-DEVICE-UUID");
            builder.UseSetting("Sideport:Scheduler:Enabled", "false");
            builder.UseSetting("Sideport:Signer:BinaryPath", signerPath);
            if (apiToken is not null)
                builder.UseSetting("Sideport:Api:AuthToken", apiToken);

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IAnisetteProvider>();
                services.AddSingleton<IAnisetteProvider>(new StubAnisette(anisetteHealthy));
            });
        });
    }

    [Fact]
    public async Task Healthz_IsOpen_AndReturnsOk()
    {
        using var factory = Factory();
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/healthz");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Readyz_AllDependenciesHealthy_Returns200Ready()
    {
        using var factory = Factory(anisetteHealthy: true);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/readyz");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ReadyDto>();
        Assert.True(body!.ready);
    }

    [Fact]
    public async Task Readyz_AnisetteDown_Returns503NotReady()
    {
        using var factory = Factory(anisetteHealthy: false);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/readyz");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ReadyDto>();
        Assert.False(body!.ready);
    }

    [Fact]
    public async Task Readyz_SignerMissing_Returns503NotReady()
    {
        using var factory = Factory(signerPath: "/nonexistent/zsign");
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/readyz");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Api_WithTokenConfigured_RejectsMissingBearer()
    {
        using var factory = Factory(apiToken: "s3cr3t-token");
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/anisette/info");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Api_WithTokenConfigured_RejectsWrongBearer()
    {
        using var factory = Factory(apiToken: "s3cr3t-token");
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "wrong");

        HttpResponseMessage response = await client.GetAsync("/api/anisette/info");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Api_WithTokenConfigured_AcceptsCorrectBearer()
    {
        using var factory = Factory(apiToken: "s3cr3t-token");
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cr3t-token");

        HttpResponseMessage response = await client.GetAsync("/api/anisette/info");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AnisetteInfo_WhenAnisetteDown_Returns503Json_NotDeveloperException()
    {
        using var factory = Factory(anisetteHealthy: false);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/anisette/info");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task OnboardingStatus_WithTokenConfigured_ReturnsFirstRunSteps()
    {
        using var factory = Factory(apiToken: "s3cr3t-token");
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cr3t-token");

        HttpResponseMessage response = await client.GetAsync("/api/onboarding/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<OnboardingDto>();
        Assert.NotNull(body);
        Assert.Contains(body!.steps, step => step.id == "api-auth" && step.state == "complete");
        Assert.Contains(body.steps, step => step.id == "anisette" && step.state == "complete");
        Assert.Contains(body.steps, step => step.id == "signer" && step.state == "complete");
        Assert.Contains(body.steps, step => step.id == "iphone-developer-mode" && step.surface == "iphone");
        Assert.False(body.firstRunComplete);
    }

    [Fact]
    public async Task Logs_ReturnRecentApiRequests()
    {
        using var factory = Factory(apiToken: "s3cr3t-token");
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cr3t-token");

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/anisette/info")).StatusCode);

        var logs = await client.GetFromJsonAsync<IReadOnlyList<LogDto>>("/api/logs?limit=20");

        Assert.NotNull(logs);
        Assert.Contains(logs!, log => log.message.Contains("/api/anisette/info", StringComparison.Ordinal));
        Assert.All(logs!, log => Assert.False(string.IsNullOrWhiteSpace(log.category)));
    }

    [Fact]
    public async Task Probes_StayOpen_EvenWhenTokenConfigured()
    {
        using var factory = Factory(apiToken: "s3cr3t-token");
        using HttpClient client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/healthz")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/readyz")).StatusCode);
    }

    [Fact]
    public async Task Api_WithoutTokenConfigured_IsOpen()
    {
        using var factory = Factory(apiToken: null);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/anisette/info");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private sealed record ReadyDto(bool ready);
    private sealed record OnboardingDto(bool firstRunComplete, IReadOnlyList<OnboardingStepDto> steps);
    private sealed record OnboardingStepDto(string id, string state, string surface);
    private sealed record LogDto(string at, string level, string category, string message);

    private sealed class StubAnisette(bool healthy) : IAnisetteProvider
    {
        public Task<AnisetteClientInfo> GetClientInfoAsync(CancellationToken ct = default) =>
            healthy
                ? Task.FromResult(new AnisetteClientInfo("<TestClient>", "akd/1.0"))
                : throw new HttpRequestException("anisette unreachable");

        public Task<AnisetteHeaders> GetHeadersAsync(CancellationToken ct = default) =>
            Task.FromResult(new AnisetteHeaders("M", "O", "R", "L", DateTimeOffset.UnixEpoch));
    }
}
