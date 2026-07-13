namespace Sideport.Orchestrator;

/// <summary>
/// Bounds which Owner-managed Apple authority a refresh may consume. Family
/// operations may reuse an in-memory session and persisted certificate, but
/// never authenticate the Owner's Apple account or create a certificate.
/// </summary>
public sealed record RefreshExecutionPolicy(
    bool AllowAppleAuthentication,
    bool AllowCertificateCreation)
{
    public static RefreshExecutionPolicy OwnerManaged { get; } = new(true, true);

    public static RefreshExecutionPolicy ExistingAuthorityOnly { get; } = new(false, false);
}
