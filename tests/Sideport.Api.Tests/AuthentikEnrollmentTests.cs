using System.Net;
using System.Text;
using System.Text.Json;
using Sideport.Api.Authentik;

namespace Sideport.Api.Tests;

public sealed class AuthentikEnrollmentTests
{
    [Fact]
    public async Task DisabledAdapter_ReturnsExistingAccountFallbackWithoutNetwork()
    {
        IdentityEnrollmentResult result = await DisabledIdentityEnrollmentAdapter.Instance.CreateAsync(new(
            "Mara",
            "mara@example.test",
            "authentik-disabled-0001",
            new Uri("https://sideport.example/login?returnUrl=%2Finvite")));

        Assert.False(result.Available);
        Assert.Null(result.EnrollmentUrl);
        Assert.Contains("not configured", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Adapter_CreatesSingleUseInvitationWithoutExposingApiToken()
    {
        const string token = "server-only-authentik-token";
        HttpRequestMessage? captured = null;
        string? body = null;
        var handler = new RecordingHandler(async request =>
        {
            captured = request;
            if (request.Method == HttpMethod.Get)
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"results\":[]}", Encoding.UTF8, "application/json") };
            body = await request.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent(
                    "{\"pk\":\"11111111-2222-3333-4444-555555555555\",\"expires\":\"2026-07-13T00:45:00Z\"}",
                    Encoding.UTF8,
                    "application/json"),
            };
        });
        var time = new FixedTimeProvider(DateTimeOffset.Parse("2026-07-13T00:30:00Z"));
        var adapter = new AuthentikEnrollmentAdapter(
            new HttpClient(handler),
            new AuthentikEnrollmentOptions(
                new Uri("https://auth.example/"),
                token,
                "sideport-enrollment",
                Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
                TimeSpan.FromMinutes(15)),
            time);

        IdentityEnrollmentResult result = await adapter.CreateAsync(new(
            "Mara",
            "mara@example.test",
            "authentik-create-0001",
            new Uri("https://sideport.example/login?returnUrl=%2Finvite")));

        Assert.True(result.Available);
        Assert.Equal(
            "https://auth.example/if/flow/sideport-enrollment/?itoken=11111111-2222-3333-4444-555555555555&next=https%3A%2F%2Fsideport.example%2Flogin%3FreturnUrl%3D%252Finvite",
            result.EnrollmentUrl!.AbsoluteUri);
        Assert.Equal("Bearer", captured!.Headers.Authorization!.Scheme);
        Assert.Equal(token, captured.Headers.Authorization.Parameter);
        Assert.DoesNotContain(token, body, StringComparison.Ordinal);
        using JsonDocument parsed = JsonDocument.Parse(body!);
        Assert.True(parsed.RootElement.GetProperty("single_use").GetBoolean());
        Assert.Equal("2026-07-13T00:45:00Z", parsed.RootElement.GetProperty("expires").GetString());
        Assert.Equal("mara@example.test", parsed.RootElement.GetProperty("fixed_data").GetProperty("email").GetString());
        Assert.Equal("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", parsed.RootElement.GetProperty("flow").GetString());
    }

    [Fact]
    public async Task Adapter_FailsWithFixedRedactedError()
    {
        var adapter = new AuthentikEnrollmentAdapter(
            new HttpClient(new RecordingHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent("secret provider detail"),
            }))),
            new AuthentikEnrollmentOptions(
                new Uri("https://auth.example/"),
                "server-only-token",
                "sideport-enrollment",
                Guid.NewGuid(),
                TimeSpan.FromMinutes(15)));

        AuthentikEnrollmentException error = await Assert.ThrowsAsync<AuthentikEnrollmentException>(() =>
            adapter.CreateAsync(new(
                "Mara",
                "mara@example.test",
                "authentik-error-0001",
                new Uri("https://sideport.example/login?returnUrl=%2Finvite"))));

        Assert.Equal("authentik-enrollment-unavailable", error.Code);
        Assert.DoesNotContain("secret provider detail", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("server-only-token", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Adapter_ReusesOnlyMatchingSingleUseInvitationForConfiguredFlow()
    {
        int posts = 0;
        Guid flow = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var handler = new RecordingHandler(request =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        $"{{\"results\":[{{\"pk\":\"11111111-2222-3333-4444-555555555555\",\"name\":\"sideport-26f5e7d9712b18c02ccf3010\",\"expires\":\"2026-07-13T00:45:00Z\",\"single_use\":true,\"flow\":\"{flow:D}\"}}]}}",
                        Encoding.UTF8,
                        "application/json"),
                });
            }
            posts++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        });
        var adapter = new AuthentikEnrollmentAdapter(
            new HttpClient(handler),
            new AuthentikEnrollmentOptions(
                new Uri("https://auth.example/"),
                "server-only-token",
                "sideport-enrollment",
                flow,
                TimeSpan.FromMinutes(15)),
            new FixedTimeProvider(DateTimeOffset.Parse("2026-07-13T00:30:00Z")));

        IdentityEnrollmentResult result = await adapter.CreateAsync(new(
            "Mara",
            "mara@example.test",
            "authentik-create-0001",
            new Uri("https://sideport.example/login?returnUrl=%2Finvite")));

        Assert.True(result.Available);
        Assert.Equal(0, posts);
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => responder(request);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
