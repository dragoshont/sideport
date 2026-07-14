using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Sideport.Api.Identity;
using Sideport.Api.WorkspaceAccess;

namespace Sideport.Api.Tests;

public sealed class NativePasskeyHttpTests
{
    private const string Origin = "https://sideport.test";
    private const string RecoveryToken = "native-passkey-recovery-token-with-enough-entropy";

    [Fact]
    public void ProfileValidation_NormalizesNameAndEmailWithoutInventingAUsername()
    {
        NativePasskeyProfile profile = NativePasskeyService.ValidateProfile(
            new NativePasskeyProfile("  Home Owner  ", "  owner@example.test  "));

        Assert.Equal("Home Owner", profile.DisplayName);
        Assert.Equal("owner@example.test", profile.Email);
    }

    [Theory]
    [InlineData("", "owner@example.test")]
    [InlineData("Home Owner", "not-an-email")]
    [InlineData("Home Owner", "")]
    public void ProfileValidation_RejectsIncompleteOrInvalidInput(string displayName, string email)
    {
        Assert.Throws<ArgumentException>(() => NativePasskeyService.ValidateProfile(
            new NativePasskeyProfile(displayName, email)));
    }

    [Fact]
    public async Task IdentityDatabase_PersistsAcrossApplicationRestarts()
    {
        string state = Path.Combine(
            Path.GetTempPath(),
            "sideport-native-passkey-persistence-tests",
            Guid.NewGuid().ToString("N"));
        try
        {
            using (var first = new NativePasskeyTestApp(state, deleteStateOnDispose: false))
            {
                await using AsyncServiceScope scope = first.Services.CreateAsyncScope();
                UserManager<SideportIdentityUser> users = scope.ServiceProvider
                    .GetRequiredService<UserManager<SideportIdentityUser>>();
                IdentityResult created = await users.CreateAsync(new SideportIdentityUser
                {
                    Id = "persistent-native-user",
                    UserName = "sideport-persistent-native-user",
                    DisplayName = "Persistent Owner",
                    Email = "persistent@example.test",
                });
                Assert.True(created.Succeeded);
            }

            using var second = new NativePasskeyTestApp(state, deleteStateOnDispose: false);
            await using AsyncServiceScope reopened = second.Services.CreateAsyncScope();
            UserManager<SideportIdentityUser> reopenedUsers = reopened.ServiceProvider
                .GetRequiredService<UserManager<SideportIdentityUser>>();
            SideportIdentityUser? user = await reopenedUsers.FindByIdAsync("persistent-native-user");
            Assert.NotNull(user);
            Assert.Equal("Persistent Owner", user.DisplayName);
        }
        finally
        {
            try { Directory.Delete(state, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public async Task AuthenticationOptions_ProjectNativeModeWithoutOidcOrAuthentik()
    {
        using var app = new NativePasskeyTestApp();
        using HttpClient client = app.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/authentication/options");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonObject body = await JsonAsync(response);
        Assert.Equal("passkey", String(body, "mode"));
        Assert.False(Bool(body, "oidcEnabled"));
        Assert.True(Bool(body, "nativePasskeyEnabled"));
        Assert.True(Bool(body, "enrollmentEnabled"));
        Assert.Equal("sideport", String(body, "enrollmentProvider"));
        Assert.Equal("Sign in with a passkey", String(body, "loginLabel"));
    }

    [Fact]
    public async Task OwnerPasskeyOptions_RequireExactOriginAndValidHandoff()
    {
        using var app = new NativePasskeyTestApp();
        using HttpClient browser = app.CreateClient();

        HttpResponseMessage missingHandoff = await SendJsonAsync(
            browser,
            "/api/workspace/owner-claims/native-passkey/options",
            new { displayName = "Home Owner", email = "owner@example.test" },
            Origin);
        Assert.Equal(HttpStatusCode.NotFound, missingHandoff.StatusCode);

        string ownerToken = app.SetupToken;
        HttpResponseMessage exchanged = await SendJsonAsync(
            browser,
            "/api/workspace/owner-claims/handoff",
            new { claimToken = ownerToken },
            Origin);
        Assert.Equal(HttpStatusCode.OK, exchanged.StatusCode);

        HttpResponseMessage wrongOrigin = await SendJsonAsync(
            browser,
            "/api/workspace/owner-claims/native-passkey/options",
            new { displayName = "Home Owner", email = "owner@example.test" },
            "https://attacker.example");
        Assert.Equal(HttpStatusCode.Forbidden, wrongOrigin.StatusCode);

        HttpResponseMessage options = await SendJsonAsync(
            browser,
            "/api/workspace/owner-claims/native-passkey/options",
            new { displayName = "Home Owner", email = "owner@example.test" },
            Origin);
        Assert.Equal(HttpStatusCode.OK, options.StatusCode);
        Assert.Contains("no-store", options.Headers.CacheControl?.ToString() ?? string.Empty);
        JsonObject body = await JsonAsync(options);
        Assert.Equal("passkey", String(body, "mode"));
        string creationOptions = String(body, "creationOptions");
        Assert.Contains("sideport.test", creationOptions, StringComparison.Ordinal);
        Assert.Contains("Home Owner", creationOptions, StringComparison.Ordinal);
        Assert.DoesNotContain("owner@example.test", creationOptions, StringComparison.Ordinal);
        Assert.Contains(
            options.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith("sideport.passkey-ceremony=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FailedAttestation_DoesNotPersistAUserOrAcceptTheOwnerClaim()
    {
        using var app = new NativePasskeyTestApp();
        using HttpClient browser = app.CreateClient();
        string ownerToken = app.SetupToken;
        Assert.Equal(HttpStatusCode.OK, (await SendJsonAsync(
            browser,
            "/api/workspace/owner-claims/handoff",
            new { claimToken = ownerToken },
            Origin)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendJsonAsync(
            browser,
            "/api/workspace/owner-claims/native-passkey/options",
            new { displayName = "Home Owner", email = "owner@example.test" },
            Origin)).StatusCode);

        HttpResponseMessage completion = await SendJsonAsync(
            browser,
            "/api/workspace/owner-claims/native-passkey/complete",
            new
            {
                displayName = "Home Owner",
                email = "owner@example.test",
                credentialJson = "{}",
                idempotencyKey = "native-owner-complete-0001",
            },
            Origin);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, completion.StatusCode);
        await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
        SideportIdentityDbContext identity = scope.ServiceProvider.GetRequiredService<SideportIdentityDbContext>();
        Assert.Equal(0, await identity.Users.CountAsync());
        var workspace = scope.ServiceProvider.GetRequiredService<WorkspaceAccess.WorkspaceAccessStore>();
        WorkspaceAccess.WorkspaceAccessDocument? document = await workspace.ReadAsync();
        Assert.NotNull(document);
        Assert.Equal(WorkspaceAccess.WorkspaceLifecycleState.BootstrapRequired, document.Workspace.State);
    }

    [Fact]
    public async Task DiscoverableLoginOptions_DoNotEnumerateUsers()
    {
        using var app = new NativePasskeyTestApp();
        using HttpClient client = app.CreateClient();

        HttpResponseMessage response = await SendJsonAsync(
            client,
            "/api/authentication/native-passkey/options",
            new { },
            Origin);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonObject body = await JsonAsync(response);
        string requestOptions = String(body, "requestOptions");
        Assert.DoesNotContain("userHandle", requestOptions, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("userName", requestOptions, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SuccessfulPasskeyEnrollmentAndDiscoverableSignIn_UseOneWorkspaceIdentity()
    {
        using var app = new NativePasskeyTestApp(useSuccessfulPasskeyHandler: true);
        using HttpClient enrollment = app.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await SendJsonAsync(
            enrollment,
            "/api/workspace/owner-claims/handoff",
            new { claimToken = app.SetupToken },
            Origin)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendJsonAsync(
            enrollment,
            "/api/workspace/owner-claims/native-passkey/options",
            new { displayName = "Home Owner", email = "owner@example.test" },
            Origin)).StatusCode);

        HttpResponseMessage completed = await SendJsonAsync(
            enrollment,
            "/api/workspace/owner-claims/native-passkey/complete",
            new
            {
                displayName = "Home Owner",
                email = "owner@example.test",
                credentialJson = "{\"id\":\"fixture-credential\"}",
                idempotencyKey = "native-owner-success-0001",
            },
            Origin);

        Assert.True(
            completed.StatusCode == HttpStatusCode.OK,
            $"Expected successful passkey enrollment, got {(int)completed.StatusCode}: {await completed.Content.ReadAsStringAsync()}");
        JsonObject firstMe = await JsonAsync(await enrollment.GetAsync("/api/me"));
        Assert.Equal("passkey", String(firstMe, "via"));
        Assert.Equal("active", String(firstMe["membership"], "state"));
        Assert.Equal("owner", String(firstMe["membership"], "role"));

        await using (AsyncServiceScope scope = app.Services.CreateAsyncScope())
        {
            UserManager<SideportIdentityUser> users = scope.ServiceProvider
                .GetRequiredService<UserManager<SideportIdentityUser>>();
            SideportIdentityUser user = Assert.Single(await users.Users.ToListAsync());
            Assert.Single(await users.GetPasskeysAsync(user));
        }

        using HttpClient returning = app.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await SendJsonAsync(
            returning,
            "/api/authentication/native-passkey/options",
            new { },
            Origin)).StatusCode);
        HttpResponseMessage signedIn = await SendJsonAsync(
            returning,
            "/api/authentication/native-passkey/complete",
            new { credentialJson = "{\"id\":\"fixture-credential\"}" },
            Origin);
        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
        JsonObject returningMe = await JsonAsync(await returning.GetAsync("/api/me"));
        Assert.Equal("passkey", String(returningMe, "via"));
        Assert.Equal("active", String(returningMe["membership"], "state"));
        Assert.Equal("owner", String(returningMe["membership"], "role"));
    }

    [Fact]
    public async Task EnrollmentCompletion_CannotChangeTheProfileBoundToTheCeremony()
    {
        using var app = new NativePasskeyTestApp(useSuccessfulPasskeyHandler: true);
        using HttpClient browser = app.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await SendJsonAsync(
            browser,
            "/api/workspace/owner-claims/handoff",
            new { claimToken = app.SetupToken },
            Origin)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendJsonAsync(
            browser,
            "/api/workspace/owner-claims/native-passkey/options",
            new { displayName = "Home Owner", email = "owner@example.test" },
            Origin)).StatusCode);

        HttpResponseMessage completion = await SendJsonAsync(
            browser,
            "/api/workspace/owner-claims/native-passkey/complete",
            new
            {
                displayName = "Home Owner",
                email = "changed@example.test",
                credentialJson = "{\"id\":\"fixture-credential\"}",
                idempotencyKey = "native-profile-mismatch-0001",
            },
            Origin);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, completion.StatusCode);
        await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
        SideportIdentityDbContext identity = scope.ServiceProvider.GetRequiredService<SideportIdentityDbContext>();
        Assert.Equal(0, await identity.Users.CountAsync());
    }

    [Fact]
    public async Task DiscoverableLoginOptions_RejectRequestsWithoutAnOrigin()
    {
        using var app = new NativePasskeyTestApp();
        using HttpClient client = app.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/authentication/native-passkey/options",
            new { });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> SendJsonAsync(
        HttpClient client,
        string path,
        object body,
        string origin)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.TryAddWithoutValidation("Origin", origin);
        return await client.SendAsync(request);
    }

    private static async Task<JsonObject> JsonAsync(HttpResponseMessage response) =>
        (await JsonNode.ParseAsync(await response.Content.ReadAsStreamAsync()))!.AsObject();

    private static string String(JsonNode? parent, string property) =>
        parent!.AsObject()[property]!.GetValue<string>();

    private static bool Bool(JsonNode? parent, string property) =>
        parent!.AsObject()[property]!.GetValue<bool>();

    private sealed class NativePasskeyTestApp : IDisposable
    {
        private readonly WebApplicationFactory<Program> _factory;
        private readonly bool _deleteStateOnDispose;

        internal NativePasskeyTestApp(
            string? stateDirectory = null,
            bool deleteStateOnDispose = true,
            bool useSuccessfulPasskeyHandler = false)
        {
            StateDirectory = stateDirectory ?? Path.Combine(
                Path.GetTempPath(),
                "sideport-native-passkey-tests",
                Guid.NewGuid().ToString("N"));
            _deleteStateOnDispose = deleteStateOnDispose;
            _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Sideport:Apple:DeviceId", "TEST-NATIVE-PASSKEY-DEVICE");
                builder.UseSetting("Sideport:Scheduler:Enabled", "false");
                builder.UseSetting("Sideport:Signer:BinaryPath", typeof(NativePasskeyHttpTests).Assembly.Location);
                builder.UseSetting("Sideport:State:Directory", StateDirectory);
                builder.UseSetting("Sideport:PublicOrigin", $"{Origin}/");
                builder.UseSetting("Sideport:Api:AuthToken", RecoveryToken);
                builder.UseSetting("Sideport:Identity:Mode", "passkey");
                builder.ConfigureServices(services => services.RemoveAll<IHostedService>());
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IOwnerSetupLinkSink>();
                    services.AddSingleton<RecordingOwnerSetupSink>();
                    services.AddSingleton<IOwnerSetupLinkSink>(sp =>
                        sp.GetRequiredService<RecordingOwnerSetupSink>());
                    if (useSuccessfulPasskeyHandler)
                    {
                        services.RemoveAll<IPasskeyHandler<SideportIdentityUser>>();
                        services.AddSingleton<IPasskeyHandler<SideportIdentityUser>, SuccessfulPasskeyHandler>();
                    }
                });
            });
        }

        internal string StateDirectory { get; }

        internal IServiceProvider Services => _factory.Services;

        internal string SetupToken
        {
            get
            {
                Uri setupUrl = Assert.Single(Services.GetRequiredService<RecordingOwnerSetupSink>().Urls);
                return setupUrl.Fragment[1..];
            }
        }

        internal HttpClient CreateClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri($"{Origin}/"),
            HandleCookies = true,
        });

        public void Dispose()
        {
            _factory.Dispose();
            if (!_deleteStateOnDispose)
                return;
            try { Directory.Delete(StateDirectory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private sealed class RecordingOwnerSetupSink : IOwnerSetupLinkSink
    {
        internal List<Uri> Urls { get; } = [];

        public void Write(Uri setupUrl) => Urls.Add(setupUrl);
    }

    private sealed class SuccessfulPasskeyHandler : IPasskeyHandler<SideportIdentityUser>
    {
        private readonly byte[] _credentialId = [1, 2, 3, 4];
        private PasskeyUserEntity? _userEntity;

        public Task<PasskeyCreationOptionsResult> MakeCreationOptionsAsync(
            PasskeyUserEntity userEntity,
            HttpContext httpContext)
        {
            _userEntity = userEntity;
            return Task.FromResult(new PasskeyCreationOptionsResult
            {
                CreationOptionsJson = "{\"challenge\":\"AQIDBA\"}",
                AttestationState = "fixture-attestation-state",
            });
        }

        public Task<PasskeyRequestOptionsResult> MakeRequestOptionsAsync(
            SideportIdentityUser? user,
            HttpContext httpContext) =>
            Task.FromResult(new PasskeyRequestOptionsResult
            {
                RequestOptionsJson = "{\"challenge\":\"BQYHCA\"}",
                AssertionState = "fixture-assertion-state",
            });

        public Task<PasskeyAttestationResult> PerformAttestationAsync(PasskeyAttestationContext context) =>
            Task.FromResult(PasskeyAttestationResult.Success(Passkey(signCount: 0), _userEntity!));

        public async Task<PasskeyAssertionResult<SideportIdentityUser>> PerformAssertionAsync(
            PasskeyAssertionContext context)
        {
            UserManager<SideportIdentityUser> users = context.HttpContext.RequestServices
                .GetRequiredService<UserManager<SideportIdentityUser>>();
            SideportIdentityUser user = await users.FindByIdAsync(_userEntity!.Id)
                ?? throw new InvalidOperationException("The fixture passkey user is missing.");
            return PasskeyAssertionResult.Success(Passkey(signCount: 1), user);
        }

        private UserPasskeyInfo Passkey(uint signCount) => new(
            _credentialId,
            [5, 6, 7, 8],
            DateTimeOffset.UtcNow,
            signCount,
            ["internal"],
            true,
            true,
            true,
            [9, 10],
            [11, 12]);
    }
}
