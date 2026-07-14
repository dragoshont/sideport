using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Sideport.Api.Identity;
using Sideport.Api.WorkspaceAccess;

namespace Sideport.Api.Tests;

public sealed class CookieStoreFailureTests
{
    [Fact]
    public async Task ValidCookie_WithUnavailableWorkspaceStore_ReturnsStructuredServiceUnavailable()
    {
        string state = Path.Combine(
            Path.GetTempPath(),
            "sideport-cookie-store-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(state);
        await File.WriteAllTextAsync(
            Path.Combine(state, WorkspaceAccessStore.FileName),
            "{ this workspace state is corrupt");
        using WebApplicationFactory<Program> factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Sideport:Apple:DeviceId", "TEST-COOKIE-STORE-DEVICE");
                builder.UseSetting("Sideport:Scheduler:Enabled", "false");
                builder.UseSetting("Sideport:Signer:BinaryPath", typeof(CookieStoreFailureTests).Assembly.Location);
                builder.UseSetting("Sideport:State:Directory", state);
                builder.UseSetting("Sideport:PublicOrigin", "https://sideport.test/");
                builder.UseSetting("Sideport:Oidc:Enabled", "true");
                builder.UseSetting("Sideport:Oidc:Authority", "https://identity.example/");
                builder.UseSetting("Sideport:Oidc:ClientId", "sideport-cookie-store-tests");
                builder.UseSetting("Sideport:Oidc:ClientSecret", "test-only-secret");
                builder.ConfigureServices(services => services.RemoveAll<IHostedService>());
            });
        IServiceProvider services = factory.Services;
        CookieAuthenticationOptions cookieOptions = services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(SideportIdentityConstants.CookieScheme);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        Claim[] claims =
        [
            new(WorkspaceRequestPrincipalResolver.ValidatedIssuerClaimType, "https://identity.example/"),
            new("sub", "family-subject"),
            new(WorkspaceRequestPrincipalResolver.SecurityEpochClaimType, "stale-epoch"),
            new(ClaimTypes.Name, "Family Person"),
        ];
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            claims,
            SideportIdentityConstants.CookieScheme));
        var ticket = new AuthenticationTicket(
            principal,
            new AuthenticationProperties
            {
                IssuedUtc = now,
                ExpiresUtc = now.AddHours(1),
            },
            SideportIdentityConstants.CookieScheme);
        string cookie = cookieOptions.TicketDataFormat.Protect(ticket);
        using HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://sideport.test/"),
            HandleCookies = false,
        });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        request.Headers.TryAddWithoutValidation("Cookie", $"sideport.session={cookie}");

        HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("workspace-store-unavailable", body.RootElement.GetProperty("error").GetString());
        Assert.Contains(
            "no-store",
            response.Headers.CacheControl?.ToString() ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
    }
}
