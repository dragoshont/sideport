namespace Sideport.DeveloperApi;

/// <summary>
/// Tuning for <see cref="PortalSigningIdentityProvider"/> — where the persisted
/// signing identity and per-call signer inputs are materialized, and when the
/// certificate is proactively re-minted.
/// </summary>
public sealed class PortalSigningOptions
{
    /// <summary>
    /// Durable, owner-private directory where Sideport persists the signing
    /// identity (the minted certificate + private key) across restarts and
    /// stages per-call signer inputs. Must NOT be a volatile temp path in
    /// production — losing it forces a certificate re-mint (free-tier quota).
    /// </summary>
    public string WorkDirectory { get; set; } =
        Path.Combine(Path.GetTempPath(), "sideport", "identities");

    /// <summary>
    /// Re-mint the development certificate when it would otherwise expire within
    /// this window. Development certs last ~1 year; the profile (not the cert) is
    /// what expires weekly, so this lead time is deliberately generous and the
    /// cert is reused across many refreshes.
    /// </summary>
    public TimeSpan CertificateRenewLeadTime { get; set; } = TimeSpan.FromDays(14);
}
