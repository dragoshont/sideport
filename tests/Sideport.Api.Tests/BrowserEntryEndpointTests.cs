using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Sideport.Api.WorkspaceAccess;

namespace Sideport.Api.Tests;

public sealed class BrowserEntryEndpointTests
{
    [Theory]
    [InlineData("/invite")]
    [InlineData("/owner-claim")]
    public async Task PrivateLinkShell_IsAnonymousAndKeepsStrictDocumentHeaders(string path)
    {
        using var app = new BrowserTestApp();
        using HttpClient client = app.CreateClient();

        HttpResponseMessage response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("sideport-private-link-shell", await response.Content.ReadAsStringAsync());
        string csp = Assert.Single(response.Headers.GetValues("Content-Security-Policy"));
        Assert.Contains("default-src 'self'", csp, StringComparison.Ordinal);
        Assert.Contains("base-uri 'none'", csp, StringComparison.Ordinal);
        Assert.Contains("object-src 'none'", csp, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", csp, StringComparison.Ordinal);
        Assert.DoesNotContain("unsafe-inline", csp, StringComparison.Ordinal);
        Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
    }

    [Theory]
    [InlineData("/assets/app.js", "window.sideportLoaded = true;")]
    [InlineData("/assets/app.css", ":root { color: black; }")]
    [InlineData("/favicon.svg", "<svg")]
    public async Task PrivateLinkShell_LocalBundleAssetsRemainAnonymous(string path, string expected)
    {
        using var app = new BrowserTestApp();
        using HttpClient client = app.CreateClient();

        HttpResponseMessage response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(expected, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/apps")]
    [InlineData("/index.html")]
    [InlineData("/private.html")]
    [InlineData("/assets/missing.js")]
    [InlineData("/logout")]
    public async Task OrdinaryAnonymousNavigation_IsStillOidcChallenged(string path)
    {
        using var app = new BrowserTestApp();
        using HttpClient client = app.CreateClient();

        HttpResponseMessage response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("identity.example", response.Headers.Location?.Host);
        Assert.Equal("/authorize", response.Headers.Location?.AbsolutePath);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RootWithoutOwner_RedirectsToOwnerClaimBeforeOidc(bool authenticated)
    {
        using var app = new BrowserTestApp();
        using HttpClient client = app.CreateClient(authenticated);

        HttpResponseMessage response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/owner-claim", response.Headers.Location?.OriginalString);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.False(response.Headers.Contains("X-Test-Return-Uri"));
    }

    [Fact]
    public async Task RootWithActiveOwner_UsesNormalOidcChallenge()
    {
        using var app = new BrowserTestApp(activeWorkspace: true);
        using HttpClient client = app.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("identity.example", response.Headers.Location?.Host);
        Assert.Equal("/authorize", response.Headers.Location?.AbsolutePath);
    }

    [Theory]
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("/", "/")]
    [InlineData("/apps", "/apps")]
    [InlineData("/apps?search=dice", "/apps?search=dice")]
    [InlineData("/invite", "/invite")]
    public async Task Login_ChallengesWithValidatedLocalReturnPath(string? returnUrl, string expected)
    {
        using var app = new BrowserTestApp();
        using HttpClient client = app.CreateClient();
        string requestPath = returnUrl is null
            ? "/login"
            : "/login?returnUrl=" + Uri.EscapeDataString(returnUrl);

        HttpResponseMessage response = await client.GetAsync(requestPath);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(expected, Assert.Single(response.Headers.GetValues("X-Test-Return-Uri")));
        Assert.Equal("identity.example", response.Headers.Location?.Host);
    }

    [Theory]
    [InlineData("https://attacker.example/")]
    [InlineData("//attacker.example/")]
    [InlineData("/%2f%2fattacker.example/")]
    [InlineData("/%252f%252fattacker.example/")]
    [InlineData("/\\attacker.example")]
    [InlineData("/%5cattacker.example")]
    [InlineData("/%255cattacker.example")]
    [InlineData("/apps\r\nLocation: https://attacker.example")]
    [InlineData("/invite#spinv1_secret")]
    [InlineData("/invite%23spinv1_secret")]
    [InlineData("/invite%2523spinv1_secret")]
    public async Task Login_RejectsUnsafeReturnPathBeforeOidcState(string returnUrl)
    {
        using var app = new BrowserTestApp();
        using HttpClient client = app.CreateClient();
        string requestPath = "/login?returnUrl=" + Uri.EscapeDataString(returnUrl);

        HttpResponseMessage response = await client.GetAsync(requestPath);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(response.Headers.Contains("X-Test-Return-Uri"));
        Assert.Null(response.Headers.Location);
        Assert.Contains("invalid-return-url", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Logout_RequiresAnAuthenticatedRequest()
    {
        using var app = new BrowserTestApp();
        using HttpClient client = app.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/logout");
        request.Headers.Add("Origin", "https://sideport.test");

        HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("identity.example", response.Headers.Location?.Host);
        Assert.Equal("/authorize", response.Headers.Location?.AbsolutePath);
    }

    [Fact]
    public async Task Logout_RejectsMissingOrigin()
    {
        using var app = new BrowserTestApp();
        using HttpClient client = app.CreateClient(authenticated: true);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/logout");

        HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("origin-or-antiforgery", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://attacker.example")]
    [InlineData("http://sideport.test")]
    [InlineData("https://sideport.test:444")]
    public async Task Logout_RejectsNonExactOriginEvenWithAntiforgeryToken(string origin)
    {
        using var app = new BrowserTestApp();
        using HttpClient client = app.CreateClient(authenticated: true);
        HttpResponseMessage status = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        string csrf = Assert.Single(status.Headers.GetValues("X-Sideport-CSRF"));
        using var request = new HttpRequestMessage(HttpMethod.Post, "/logout");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("X-Sideport-CSRF", csrf);

        HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("origin-or-antiforgery", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Logout_RejectsSameOriginRequestWithoutAntiforgeryToken()
    {
        using var app = new BrowserTestApp();
        using HttpClient client = app.CreateClient(authenticated: true);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/logout");
        request.Headers.Add("Origin", "https://sideport.test");

        HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("origin-or-antiforgery", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Logout_WithSameOriginAndAntiforgeryToken_UsesOidcSignoutCallback()
    {
        using var app = new BrowserTestApp();
        using HttpClient client = app.CreateClient(authenticated: true);
        HttpResponseMessage status = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        string csrf = Assert.Single(status.Headers.GetValues("X-Sideport-CSRF"));

        using var request = new HttpRequestMessage(HttpMethod.Post, "/logout");
        request.Headers.Add("Origin", "https://sideport.test");
        request.Headers.Add("X-Sideport-CSRF", csrf);

        HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", Assert.Single(response.Headers.GetValues("X-Test-Signout-Return-Uri")));
        Assert.Equal("identity.example", response.Headers.Location?.Host);
        Assert.Equal("/logout", response.Headers.Location?.AbsolutePath);
        string location = response.Headers.Location?.OriginalString ?? string.Empty;
        Assert.Contains("post_logout_redirect_uri=", location, StringComparison.Ordinal);
        Assert.Contains(Uri.EscapeDataString("https://sideport.test/signout-callback-oidc"), location, StringComparison.Ordinal);
    }

    private sealed class BrowserTestApp : IDisposable
    {
        private readonly string _root;
        private readonly WebApplicationFactory<Program> _factory;

        public BrowserTestApp(bool activeWorkspace = false)
        {
            _root = Path.Combine(Path.GetTempPath(), "sideport-browser-entry-tests", Guid.NewGuid().ToString("N"));
            string webRoot = Path.Combine(_root, "wwwroot");
            string assets = Path.Combine(webRoot, "assets");
            Directory.CreateDirectory(assets);
            File.WriteAllText(
                Path.Combine(webRoot, "index.html"),
                "<main id=\"sideport-private-link-shell\"></main><script src=\"/assets/app.js\"></script>");
            File.WriteAllText(Path.Combine(webRoot, "private.html"), "private html must not become public");
            File.WriteAllText(Path.Combine(assets, "app.js"), "window.sideportLoaded = true;");
            File.WriteAllText(Path.Combine(assets, "app.css"), ":root { color: black; }");
            File.WriteAllText(Path.Combine(webRoot, "favicon.svg"), "<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>");

            string stateDirectory = Path.Combine(_root, "state");
            if (activeWorkspace)
                BootstrapWorkspace(stateDirectory);

            _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseWebRoot(webRoot);
                builder.UseSetting("Sideport:Apple:DeviceId", "TEST-BROWSER-ENTRY-DEVICE");
                builder.UseSetting("Sideport:Scheduler:Enabled", "false");
                builder.UseSetting("Sideport:Signer:BinaryPath", typeof(BrowserEntryEndpointTests).Assembly.Location);
                builder.UseSetting("Sideport:State:Directory", stateDirectory);
                builder.UseSetting("Sideport:Oidc:Enabled", "true");
                builder.UseSetting("Sideport:Oidc:Authority", "https://identity.example");
                builder.UseSetting("Sideport:Oidc:ClientId", "sideport-browser-tests");
                builder.UseSetting("Sideport:Oidc:ClientSecret", "test-only-secret");
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IHostedService>();
                    services.AddAuthentication(options =>
                        {
                            options.DefaultAuthenticateScheme = HeaderAuthenticationHandler.SchemeName;
                        })
                        .AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>(
                            HeaderAuthenticationHandler.SchemeName,
                            _ => { });
                    services.PostConfigure<OpenIdConnectOptions>(
                        OpenIdConnectDefaults.AuthenticationScheme,
                        options =>
                        {
                            var configuration = new OpenIdConnectConfiguration
                            {
                                Issuer = "https://identity.example",
                                AuthorizationEndpoint = "https://identity.example/authorize",
                                EndSessionEndpoint = "https://identity.example/logout",
                                TokenEndpoint = "https://identity.example/token",
                            };
                            options.ConfigurationManager =
                                new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
                            options.Events.OnRedirectToIdentityProvider = context =>
                            {
                                context.Response.Headers["X-Test-Return-Uri"] = context.Properties.RedirectUri;
                                return Task.CompletedTask;
                            };
                            options.Events.OnRedirectToIdentityProviderForSignOut = context =>
                            {
                                context.Response.Headers["X-Test-Signout-Return-Uri"] = context.Properties.RedirectUri;
                                return Task.CompletedTask;
                            };
                        });
                });
            });
        }

        private static void BootstrapWorkspace(string stateDirectory)
        {
            var store = new WorkspaceAccessStore(stateDirectory);
            WorkspaceOwnerClaimCreateResult claim = store.CreateOwnerClaimAsync(new(
                ExpectedOwnerMemberId: null,
                ImpactVersion: null,
                Lifetime: TimeSpan.FromMinutes(15),
                IdempotencyKey: "browser-active-owner-claim",
                RequestId: "req-browser-active-owner-claim")).GetAwaiter().GetResult();
            WorkspaceHandoffCreateResult handoff = store.ExchangeOwnerClaimAsync(
                claim.Token!,
                "req-browser-active-handoff").GetAwaiter().GetResult();
            store.AcceptOwnerClaimAsync(
                handoff.Token,
                new WorkspaceAcceptanceRequest(
                    new WorkspaceIdentityKey("https://identity.example", "owner-subject"),
                    "Sideport Owner",
                    "owner@example.test",
                    "browser-active-owner-accept",
                    "req-browser-active-owner-accept")).GetAwaiter().GetResult();
        }

        public HttpClient CreateClient(bool authenticated = false)
        {
            HttpClient client = _factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("https://sideport.test"),
                HandleCookies = true,
            });
            if (authenticated)
                client.DefaultRequestHeaders.Add(HeaderAuthenticationHandler.UserHeader, "owner-subject");
            return client;
        }

        public void Dispose()
        {
            _factory.Dispose();
            try { Directory.Delete(_root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private sealed class HeaderAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "browser-entry-test";
        public const string UserHeader = "X-Test-User";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            string? subject = Request.Headers[UserHeader].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(subject))
                return Task.FromResult(AuthenticateResult.NoResult());

            Claim[] claims =
            [
                new Claim("sub", subject),
                new Claim("iss", "https://identity.example"),
                new Claim(
                    WorkspaceRequestPrincipalResolver.ValidatedIssuerClaimType,
                    "https://identity.example"),
                new Claim(ClaimTypes.NameIdentifier, subject),
                new Claim(ClaimTypes.Name, "Sideport Owner"),
            ];
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
        }
    }
}
