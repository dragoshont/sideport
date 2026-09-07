using Sideport.Core;

namespace Sideport.Orchestrator;

/// <summary>
/// Bounds which Owner-managed Apple authority a refresh may consume. Family
/// operations may reuse an in-memory session and persisted certificate, but
/// never authenticate the Owner's Apple account or create a certificate.
/// </summary>
public sealed record RefreshExecutionPolicy(
    bool AllowAppleAuthentication,
    bool AllowCertificateCreation,
    DeviceConnection? RequiredInstallConnection = null)
{
    public static RefreshExecutionPolicy OwnerManaged { get; } = new(true, true);

    public static RefreshExecutionPolicy ExistingAuthorityOnly { get; } = new(false, false);

    /// <summary>
    /// Superseding-renewal recovery. It may authenticate the Owner's Apple
    /// authority to reuse the persisted signing identity, but must never create
    /// a certificate: if the saved identity is not reusable it fails closed.
    /// </summary>
    public static RefreshExecutionPolicy RecoveryRenewal { get; } = new(true, false);

    public RefreshRecoveryPlan? Recovery { get; init; }
}
