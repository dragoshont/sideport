namespace Sideport.Core;

/// <summary>
/// Anisette / ADI provider — the irreducible non-managed constraint (design §5).
///
/// Apple's CoreADI is closed, per-architecture Android code that cannot be
/// reimplemented in managed .NET. Sideport speaks to it over the stable
/// anisette v3 HTTP contract so the dependency stays a separate process
/// (also a license firewall — Provision is LGPL/AGPL-adjacent).
/// </summary>
public interface IAnisetteProvider
{
    /// <summary>Static client identifiers (<c>/v3/client_info</c>).</summary>
    Task<AnisetteClientInfo> GetClientInfoAsync(CancellationToken ct = default);

    /// <summary>
    /// Per-request anisette headers (<c>X-Apple-I-MD*</c>) GrandSlam requires
    /// (<c>/v3/get_headers</c>).
    /// </summary>
    Task<AnisetteHeaders> GetHeadersAsync(CancellationToken ct = default);
}

/// <summary>Static anisette client metadata.</summary>
public sealed record AnisetteClientInfo(string ClientInfo, string UserAgent);

/// <summary>The one-time anisette headers attached to a GrandSlam request.</summary>
public sealed record AnisetteHeaders(
    string MachineId,        // X-Apple-I-MD-M
    string OneTimePassword,  // X-Apple-I-MD
    string RoutingInfo,      // X-Apple-I-MD-RINFO
    string LocalUserId,      // X-Apple-I-MD-LU
    DateTimeOffset ClientTime);
