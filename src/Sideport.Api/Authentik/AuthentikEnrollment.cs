using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Sideport.Api.Authentik;

internal sealed record AuthentikEnrollmentOptions(
    Uri? BaseUrl,
    string? ApiToken,
    string EnrollmentFlowSlug,
    Guid? EnrollmentFlowId,
    TimeSpan InvitationLifetime,
    Uri? ReturnUrl = null)
{
    internal bool Enabled => BaseUrl is not null && !string.IsNullOrWhiteSpace(ApiToken) && EnrollmentFlowId is not null;
}

internal sealed record AuthentikAuthenticationOptions(
    bool OidcEnabled,
    bool EnrollmentEnabled,
    Uri ExistingAccountUrl,
    Uri? RecoveryUrl,
    string ProviderId = "oidc",
    string ProviderLabel = "Identity provider",
    string LoginLabel = "Continue to sign in");

internal sealed record AuthentikEnrollmentRequest(
    string DisplayName,
    string ContactEmail,
    string IdempotencyKey);

internal sealed record AuthentikEnrollmentResult(
    bool Available,
    Uri? EnrollmentUrl,
    DateTimeOffset? ExpiresAt,
    string? Reason);

internal interface IAuthentikEnrollmentAdapter
{
    Task<AuthentikEnrollmentResult> CreateAsync(
        AuthentikEnrollmentRequest request,
        CancellationToken ct = default);
}

internal sealed class DisabledAuthentikEnrollmentAdapter : IAuthentikEnrollmentAdapter
{
    internal static DisabledAuthentikEnrollmentAdapter Instance { get; } = new();

    public Task<AuthentikEnrollmentResult> CreateAsync(
        AuthentikEnrollmentRequest request,
        CancellationToken ct = default) =>
        Task.FromResult(new AuthentikEnrollmentResult(
            Available: false,
            EnrollmentUrl: null,
            ExpiresAt: null,
            Reason: "Sign in with an existing Authentik account. New-account enrollment is not configured."));
}

internal sealed class AuthentikEnrollmentAdapter(
    HttpClient http,
    AuthentikEnrollmentOptions options,
    TimeProvider? timeProvider = null) : IAuthentikEnrollmentAdapter
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<AuthentikEnrollmentResult> CreateAsync(
        AuthentikEnrollmentRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!options.Enabled || options.BaseUrl is null || string.IsNullOrWhiteSpace(options.ApiToken))
            return await DisabledAuthentikEnrollmentAdapter.Instance.CreateAsync(request, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 128)
            throw new ArgumentException("A bounded enrollment request key is required.", nameof(request));

        string name = $"sideport-{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(request.IdempotencyKey)))[..24].ToLowerInvariant()}";
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
                return Result(existing.Pk, existing.Expires);
            }

            DateTimeOffset expiresAt = _time.GetUtcNow().Add(options.InvitationLifetime);
            var payload = new AuthentikInvitationCreate(
            Name: name,
            Expires: expiresAt,
            FixedData: new Dictionary<string, object?>
            {
                ["name"] = request.DisplayName,
                ["email"] = request.ContactEmail,
                ["username"] = request.ContactEmail,
            },
            SingleUse: true,
            Flow: options.EnrollmentFlowId!.Value);

            using var message = Request(HttpMethod.Post, "/api/v3/stages/invitation/invitations/");
            message.Content = JsonContent.Create(payload);

            using HttpResponseMessage response = await http.SendAsync(message, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw Unavailable();
            AuthentikInvitationCreated? created = await response.Content
                .ReadFromJsonAsync<AuthentikInvitationCreated>(cancellationToken: ct)
                .ConfigureAwait(false);
            if (created?.Pk == Guid.Empty)
                throw Unavailable();
            return Result(created!.Pk, created.Expires == default ? expiresAt : created.Expires);
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

    private AuthentikEnrollmentResult Result(Guid pk, DateTimeOffset expiresAt)
    {
        string path = $"/if/flow/{Uri.EscapeDataString(options.EnrollmentFlowSlug)}/?itoken={pk:D}";
        if (options.ReturnUrl is not null)
            path += $"&next={Uri.EscapeDataString(options.ReturnUrl.ToString())}";
        Uri enrollmentUrl = new(options.BaseUrl!, path);
        return new(true, enrollmentUrl, expiresAt, Reason: null);
    }

    private static AuthentikEnrollmentException Unavailable() => new(
        "authentik-enrollment-unavailable",
        "Authentik could not create a passkey enrollment link.");

    private sealed record AuthentikInvitationCreate(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("expires")] DateTimeOffset Expires,
        [property: JsonPropertyName("fixed_data")] IReadOnlyDictionary<string, object?> FixedData,
        [property: JsonPropertyName("single_use")] bool SingleUse,
        [property: JsonPropertyName("flow")] Guid Flow);

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
