using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sideport.Api.DeviceInventory;
using Sideport.Api.Operations;
using Sideport.Api.WorkspaceAccess;

namespace Sideport.Api.Tests;

public sealed class OperationHttpActorTests
{
    [Fact]
    public async Task FamilyRetry_OlderEnrollmentAfterAcceptedPhone_RequiresOwnerAndCreatesNoWork()
    {
        string stateDirectory = Path.Combine(
            Path.GetTempPath(),
            "sideport-http-actor-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stateDirectory);
        try
        {
            using WebApplicationFactory<Program> factory = Factory(stateDirectory);
            string ownerMemberId = await BootstrapOwnerAsync(factory);
            string familyMemberId = await AddFamilyAsync(factory, ownerMemberId);
            OperationStore operations = factory.Services.GetRequiredService<OperationStore>();
            KnownDeviceStore devices = factory.Services.GetRequiredService<KnownDeviceStore>();
            DateTimeOffset now = DateTimeOffset.UtcNow;
            const string sourceOperationId = "op_enroll_family_older_phone";

            await devices.UpsertAsync(new KnownDeviceRecord(
                "FAMILY-CURRENT-PHONE",
                "Family iPhone",
                "iPhone17,1",
                "18.5",
                "usb",
                now.AddDays(-1),
                now,
                "live-poll",
                now,
                "trusted",
                null,
                null,
                now,
                InventoryState: "accepted",
                AcceptedAt: now.AddDays(-1),
                AcceptedBy: "Home Owner",
                EnrollmentOperationId: "op_enroll_family_current_phone",
                LockdownCheckedAt: now,
                UsableForInstall: true,
                OwnerMemberId: familyMemberId));
            await operations.AddIfIdempotentMissingAsync(new OperationRecordDto(
                sourceOperationId,
                DeviceEnrollmentService.OperationType,
                "failed",
                now.AddDays(-2),
                now.AddDays(-2),
                now.AddDays(-2),
                now.AddDays(-2),
                new OperationActorDto("member", "Family Person", familyMemberId),
                "family-older-phone",
                1,
                new OperationTargetDto("FAMILY-OLDER-PHONE", null, Kind: "device-enrollment"),
                [],
                null,
                new OperationIssueDto("device-lockdown-untrusted", "Trust was not completed."),
                Cancelable: false,
                Retryable: true,
                Rerunnable: false,
                CorrelationId: sourceOperationId,
                ActorMemberId: familyMemberId,
                OwnerMemberId: familyMemberId));

            using HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
            });
            SetIdentity(
                client,
                "https://identity.example/tenant-a",
                "family-1",
                "Family Person");
            string csrf = await GetCsrfAsync(client);
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"/api/operations/{sourceOperationId}/retry")
            {
                Content = JsonContent.Create(new { idempotencyKey = "family-older-phone-retry" }),
            };
            request.Headers.Add("Origin", "https://localhost");
            request.Headers.Add("X-Sideport-CSRF", csrf);

            HttpResponseMessage response = await client.SendAsync(request);
            OwnerActionResponse error = (await response.Content.ReadFromJsonAsync<OwnerActionResponse>())!;

            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
            Assert.Equal("owner-action-required", error.error);
            Assert.Equal("additional-device-owner-required", error.reason);
            OperationRecordDto onlyOperation = Assert.Single(await operations.ListAsync(limit: null));
            Assert.Equal(sourceOperationId, onlyOperation.OperationId);
            Assert.DoesNotContain(
                await operations.ListAsync(limit: null),
                operation => string.Equals(operation.ParentOperationId, sourceOperationId, StringComparison.Ordinal));
        }
        finally
        {
            try { Directory.Delete(stateDirectory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public async Task OidcHttpActor_UsesDurableMemberId_AndRejectsUnknownIdentities()
    {
        string stateDirectory = Path.Combine(
            Path.GetTempPath(),
            "sideport-http-actor-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stateDirectory);
        try
        {
            using WebApplicationFactory<Program> factory = Factory(stateDirectory);
            string ownerMemberId = await BootstrapOwnerAsync(factory);
            using HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
            });

            OperationResponse first = await StartEnrollmentAsync(
                client,
                issuer: "https://identity.example/tenant-a",
                subject: "owner-1",
                displayName: "Family Admin",
                ownerMemberId,
                expectedStatus: HttpStatusCode.Accepted);
            Assert.Equal("member", first.actor.kind);
            Assert.Equal(ownerMemberId, first.actor.id);

            OperationResponse renamedReplay = await StartEnrollmentAsync(
                client,
                issuer: "https://identity.example/tenant-a",
                subject: "owner-1",
                displayName: "Renamed Family Admin",
                ownerMemberId,
                expectedStatus: HttpStatusCode.OK);
            Assert.Equal(first.operationId, renamedReplay.operationId);
            Assert.Equal(first.actor.id, renamedReplay.actor.id);

            await AssertUnknownIdentityDeniedAsync(
                client,
                issuer: "https://identity.example/tenant-a",
                subject: "owner-2",
                displayName: "Family Admin");

            await AssertUnknownIdentityDeniedAsync(
                client,
                issuer: "https://identity.example/tenant-b",
                subject: "owner-1",
                displayName: "Family Admin");
        }
        finally
        {
            try { Directory.Delete(stateDirectory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static WebApplicationFactory<Program> Factory(string stateDirectory) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Sideport:Oidc:Enabled", "true");
            builder.UseSetting("Sideport:Oidc:Authority", "https://identity.example");
            builder.UseSetting("Sideport:Oidc:ClientId", "sideport-tests");
            builder.UseSetting("Sideport:Oidc:ClientSecret", "test-only-secret");
            builder.UseSetting("Sideport:Apple:DeviceId", "TEST-OIDC-ACTOR-DEVICE");
            builder.UseSetting("Sideport:Scheduler:Enabled", "false");
            builder.UseSetting("Sideport:Signer:BinaryPath", typeof(OperationHttpActorTests).Assembly.Location);
            builder.UseSetting("Sideport:State:Directory", stateDirectory);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = HeaderOidcAuthHandler.SchemeName;
                        options.DefaultScheme = HeaderOidcAuthHandler.SchemeName;
                        options.DefaultChallengeScheme = HeaderOidcAuthHandler.SchemeName;
                    })
                    .AddScheme<AuthenticationSchemeOptions, HeaderOidcAuthHandler>(
                        HeaderOidcAuthHandler.SchemeName,
                        _ => { });
            });
        });

    private static async Task<OperationResponse> StartEnrollmentAsync(
        HttpClient client,
        string issuer,
        string subject,
        string displayName,
        string ownerMemberId,
        HttpStatusCode expectedStatus)
    {
        SetIdentity(client, issuer, subject, displayName);
        string csrf = await GetCsrfAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/devices/enrollments")
        {
            Content = JsonContent.Create(new
            {
                idempotencyKey = "same-browser-action",
                targetMemberId = ownerMemberId,
            }),
        };
        request.Headers.Add("Origin", "https://localhost");
        request.Headers.Add("X-Sideport-CSRF", csrf);
        HttpResponseMessage response = await client.SendAsync(request);
        Assert.Equal(expectedStatus, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<OperationResponse>())!;
    }

    private static async Task AssertUnknownIdentityDeniedAsync(
        HttpClient client,
        string issuer,
        string subject,
        string displayName)
    {
        SetIdentity(client, issuer, subject, displayName);
        HttpResponseMessage response = await client.GetAsync("/api/devices/known");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("workspace-membership-required", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    private static async Task<string> GetCsrfAsync(HttpClient client)
    {
        HttpResponseMessage response = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return Assert.Single(response.Headers.GetValues("X-Sideport-CSRF"));
    }

    private static async Task<string> BootstrapOwnerAsync(WebApplicationFactory<Program> factory)
    {
        WorkspaceAccessStore store = factory.Services.GetRequiredService<WorkspaceAccessStore>();
        WorkspaceOwnerClaimCreateResult claim = await store.CreateOwnerClaimAsync(
            new WorkspaceOwnerClaimCreateRequest(
                null,
                null,
                TimeSpan.FromMinutes(15),
                "http-actor-owner-claim",
                "http-actor-bootstrap"));
        WorkspaceHandoffCreateResult handoff = await store.ExchangeOwnerClaimAsync(
            claim.Token!,
            "http-actor-handoff");
        WorkspaceAcceptanceResult accepted = await store.AcceptOwnerClaimAsync(
            handoff.Token,
            new WorkspaceAcceptanceRequest(
                new WorkspaceIdentityKey("https://identity.example/tenant-a", "owner-1"),
                "Family Admin",
                null,
                "http-actor-owner-accept",
                "http-actor-accept"));
        return accepted.Member.MemberId;
    }

    private static async Task<string> AddFamilyAsync(
        WebApplicationFactory<Program> factory,
        string ownerMemberId)
    {
        WorkspaceAccessStore store = factory.Services.GetRequiredService<WorkspaceAccessStore>();
        WorkspaceInvitationCreateResult invitation = await store.CreateInvitationAsync(
            new WorkspaceInvitationCreateRequest(
                WorkspaceActorRecord.ForMember(ownerMemberId),
                "Family Person",
                "family@example.test",
                TimeSpan.FromDays(7),
                "http-actor-family-invitation",
                "http-actor-family-invitation-create"));
        WorkspaceHandoffCreateResult handoff = await store.ExchangeInvitationAsync(
            invitation.Token!,
            "http-actor-family-handoff");
        WorkspaceAcceptanceResult accepted = await store.AcceptInvitationAsync(
            handoff.Token,
            new WorkspaceAcceptanceRequest(
                new WorkspaceIdentityKey("https://identity.example/tenant-a", "family-1"),
                "Family Person",
                "family@example.test",
                "http-actor-family-accept",
                "http-actor-family-accept-request"));
        return accepted.Member.MemberId;
    }

    private static void SetIdentity(
        HttpClient client,
        string issuer,
        string subject,
        string displayName)
    {
        client.DefaultRequestHeaders.Remove(HeaderOidcAuthHandler.IssuerHeader);
        client.DefaultRequestHeaders.Remove(HeaderOidcAuthHandler.SubjectHeader);
        client.DefaultRequestHeaders.Remove(HeaderOidcAuthHandler.NameHeader);
        client.DefaultRequestHeaders.Add(HeaderOidcAuthHandler.IssuerHeader, issuer);
        client.DefaultRequestHeaders.Add(HeaderOidcAuthHandler.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(HeaderOidcAuthHandler.NameHeader, displayName);
    }

    private sealed record OperationActor(string kind, string displayName, string id);
    private sealed record OperationResponse(string operationId, OperationActor actor);
    private sealed record OwnerActionResponse(string error, string reason, string message);

    private sealed class HeaderOidcAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        WorkspaceAccessStore workspace)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "header-oidc-test";
        public const string IssuerHeader = "X-Test-Oidc-Issuer";
        public const string SubjectHeader = "X-Test-Oidc-Subject";
        public const string NameHeader = "X-Test-Oidc-Name";

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            string? issuer = Request.Headers[IssuerHeader].FirstOrDefault();
            string? subject = Request.Headers[SubjectHeader].FirstOrDefault();
            string? displayName = Request.Headers[NameHeader].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(issuer) ||
                string.IsNullOrWhiteSpace(subject) ||
                string.IsNullOrWhiteSpace(displayName))
            {
                return AuthenticateResult.NoResult();
            }

            var claims = new List<Claim>
            {
                new Claim("iss", issuer),
                new Claim("sub", subject),
                new Claim(WorkspaceRequestPrincipalResolver.ValidatedIssuerClaimType, issuer),
                new Claim(ClaimTypes.Name, displayName),
            };
            WorkspaceAccessDocument? document = await workspace.ReadAsync(Context.RequestAborted);
            if (document?.Workspace.State == WorkspaceLifecycleState.Active)
            {
                claims.Add(new Claim(
                    WorkspaceRequestPrincipalResolver.SecurityEpochClaimType,
                    document.Workspace.SecurityEpoch));
            }
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
            return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
        }
    }
}
