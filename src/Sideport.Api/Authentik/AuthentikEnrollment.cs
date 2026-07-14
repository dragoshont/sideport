using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Sideport.Api.Authentik;

internal sealed record AuthentikEnrollmentOptions(
    Uri? BaseUrl,
    string? ApiToken,
    string EnrollmentFlowSlug,
    Guid? EnrollmentFlowId,
    TimeSpan InvitationLifetime)
{
    internal bool Enabled => BaseUrl is not null && !string.IsNullOrWhiteSpace(ApiToken) && EnrollmentFlowId is not null;
}

internal sealed record IdentityAuthenticationOptions(
    bool OidcEnabled,
    bool EnrollmentEnabled,
    Uri ExistingAccountUrl,
    Uri? RecoveryUrl,
    string ProviderId = "oidc",
    string ProviderLabel = "Identity provider",
    string LoginLabel = "Continue to sign in",
    string EnrollmentLabel = "Create passkey",
    string PreferredMethod = "login",
    string? EnrollmentProviderId = null,
    string Mode = "oidc",
    bool NativePasskeyEnabled = false);

internal sealed record IdentityEnrollmentRequest(
    string? DisplayName,
    string? ContactEmail,
    string IdempotencyKey,
    Uri ReturnUrl);

internal sealed record IdentityEnrollmentResult(
    bool Available,
    Uri? EnrollmentUrl,
    DateTimeOffset? ExpiresAt,
    string? Reason);

internal interface IIdentityEnrollmentAdapter
{
    Task<IdentityEnrollmentResult> CreateAsync(
        IdentityEnrollmentRequest request,
        CancellationToken ct = default);
}

internal sealed class DisabledIdentityEnrollmentAdapter : IIdentityEnrollmentAdapter
{
    internal static DisabledIdentityEnrollmentAdapter Instance { get; } = new();

    public Task<IdentityEnrollmentResult> CreateAsync(
        IdentityEnrollmentRequest request,
        CancellationToken ct = default) =>
        Task.FromResult(new IdentityEnrollmentResult(
            Available: false,
            EnrollmentUrl: null,
            ExpiresAt: null,
            Reason: "Passkey enrollment is not configured. Use the existing-account sign-in option."));
}

internal sealed class AuthentikEnrollmentAdapter(
    HttpClient http,
    AuthentikEnrollmentOptions options,
    ILogger<AuthentikEnrollmentAdapter> logger,
    TimeProvider? timeProvider = null) : IIdentityEnrollmentAdapter
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<IdentityEnrollmentResult> CreateAsync(
        IdentityEnrollmentRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!options.Enabled || options.BaseUrl is null || string.IsNullOrWhiteSpace(options.ApiToken))
            return await DisabledIdentityEnrollmentAdapter.Instance.CreateAsync(request, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 128)
            throw new ArgumentException("A bounded enrollment request key is required.", nameof(request));

        string name = EnrollmentNameFor(request.IdempotencyKey);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            AuthentikInvitationCreated? existing = await FindExistingAsync(name, ct).ConfigureAwait(false);
            DateTimeOffset now = _time.GetUtcNow();
            if (existing is not null &&
                existing.Expires > now &&
                existing.Expires <= now.Add(options.InvitationLifetime).Add(TimeSpan.FromSeconds(30)) &&
                existing.SingleUse &&
                existing.Flow == options.EnrollmentFlowId)
            {
                return Result(existing.Pk, existing.Expires, request.ReturnUrl);
            }

            DateTimeOffset expiresAt = _time.GetUtcNow().Add(options.InvitationLifetime);
            var fixedData = new Dictionary<string, object?>
            {
                // Authentik requires a username for user creation, but Sideport
                // identities are passkey-first and users should never invent an
                // infrastructure identifier. The invitation name is already a
                // deterministic, opaque hash of the bounded idempotency key.
                ["username"] = name,
            };
            if (!string.IsNullOrWhiteSpace(request.DisplayName))
                fixedData["name"] = request.DisplayName;
            if (!string.IsNullOrWhiteSpace(request.ContactEmail))
            {
                fixedData["email"] = request.ContactEmail;
            }
            var fixedDataJson = new JsonObject();
            foreach ((string key, object? value) in fixedData)
                fixedDataJson[key] = JsonValue.Create(value as string);
            var payload = new JsonObject
            {
                ["name"] = name,
                ["expires"] = expiresAt.UtcDateTime.ToString(
                    "yyyy-MM-dd'T'HH:mm:ss'Z'",
                    System.Globalization.CultureInfo.InvariantCulture),
                ["fixed_data"] = fixedDataJson,
                ["single_use"] = true,
                ["flow"] = options.EnrollmentFlowId!.Value.ToString("D"),
            };

            using var message = Request(HttpMethod.Post, "/api/v3/stages/invitation/invitations/");
            message.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");

            using HttpResponseMessage response = await http.SendAsync(message, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                string detail = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                logger.LogWarning(
                    "Identity enrollment provider rejected an invitation with HTTP {StatusCode}: {Detail}",
                    (int)response.StatusCode,
                    detail.Length <= 512 ? detail : detail[..512]);
                throw Unavailable();
            }
            AuthentikInvitationCreated? created = await response.Content
                .ReadFromJsonAsync<AuthentikInvitationCreated>(cancellationToken: ct)
                .ConfigureAwait(false);
            if (created?.Pk == Guid.Empty)
                throw Unavailable();
            return Result(created!.Pk, created.Expires == default ? expiresAt : created.Expires, request.ReturnUrl);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<AuthentikInvitationCreated?> FindExistingAsync(string name, CancellationToken ct)
    {
        using var message = Request(HttpMethod.Get, $"/api/v3/stages/invitation/invitations/?name={Uri.EscapeDataString(name)}");
        using HttpResponseMessage response = await http.SendAsync(message, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw Unavailable();
        AuthentikInvitationList? list = await response.Content
            .ReadFromJsonAsync<AuthentikInvitationList>(cancellationToken: ct)
            .ConfigureAwait(false);
        return list?.Results.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.Ordinal));
    }

    private HttpRequestMessage Request(HttpMethod method, string path)
    {
        var message = new HttpRequestMessage(method, new Uri(options.BaseUrl!, path));
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiToken);
        return message;
    }

    private IdentityEnrollmentResult Result(Guid pk, DateTimeOffset expiresAt, Uri? returnUrl = null)
    {
        string path = $"/if/flow/{Uri.EscapeDataString(options.EnrollmentFlowSlug)}/?itoken={pk:D}";
        if (returnUrl is not null)
            path += $"&next={Uri.EscapeDataString(returnUrl.ToString())}";
        Uri enrollmentUrl = new(options.BaseUrl!, path);
        return new(true, enrollmentUrl, expiresAt, Reason: null);
    }

    private static AuthentikEnrollmentException Unavailable() => new(
        "authentik-enrollment-unavailable",
        "Authentik could not create a passkey enrollment link.");

    internal static string EnrollmentNameFor(string idempotencyKey) =>
        $"sideport-{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(idempotencyKey)))[..24].ToLowerInvariant()}";

    private sealed record AuthentikInvitationCreated(
        [property: JsonPropertyName("pk")] Guid Pk,
        [property: JsonPropertyName("name")] string? Name = null,
        [property: JsonPropertyName("expires")] DateTimeOffset Expires = default,
        [property: JsonPropertyName("single_use")] bool SingleUse = false,
        [property: JsonPropertyName("flow")] Guid? Flow = null);

    private sealed record AuthentikInvitationList(
        [property: JsonPropertyName("results")] IReadOnlyList<AuthentikInvitationCreated> Results);
}

internal sealed class AuthentikEnrollmentException(
    string code,
    string message,
    Exception? inner = null) : Exception(message, inner)
{
    internal string Code { get; } = code;
}
