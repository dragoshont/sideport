namespace Sideport.Orchestrator;

/// <summary>Tuning for the refresh orchestrator + scheduler.</summary>
public sealed class OrchestratorOptions
{
    /// <summary>
    /// Durable Sideport state directory. In production this should be backed by
    /// a PVC/volume. The API stores app registrations here before any refresh
    /// scheduler can rely on them.
    /// </summary>
    public string StateDirectory { get; set; } =
        Path.Combine(Path.GetTempPath(), "sideport");

    /// <summary>Path to the durable app-registration JSON file.</summary>
    public string AppRegistryPath => Path.Combine(StateDirectory, "apps.json");

    /// <summary>
    /// Durable directory for the INPUT IPAs registrations point at (one per app),
    /// on the same PVC as <see cref="AppRegistryPath"/>. Copying the IPA here at
    /// registration time is what lets the scheduler re-sign unattended after a
    /// pod restart wipes the ephemeral upload path.
    /// </summary>
    public string IpaStoreDirectory => Path.Combine(StateDirectory, "ipas");

    /// <summary>
    /// Directory where signed output IPAs are written. Defaults to a Sideport
    /// work directory under the system temp path.
    /// </summary>
    public string WorkDirectory { get; set; } =
        Path.Combine(Path.GetTempPath(), "sideport", "signed");

    /// <summary>
    /// How long before a signature expires the scheduler proactively refreshes
    /// it. Free-tier certs last ~7 days, so the default leads by 2 days.
    /// </summary>
    public TimeSpan RefreshLeadTime { get; set; } = TimeSpan.FromDays(2);

    /// <summary>How often the scheduler evaluates the catalog for due refreshes.</summary>
    public TimeSpan ScheduleInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Minimum wait before retrying an app whose last refresh failed,
    /// so an unreachable device cannot hot-loop the signer.</summary>
    public TimeSpan RetryBackoff { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Optional fixed re-sign cadence. When set, an app is re-signed once its
    /// last SUCCESSFUL sign is older than this — even if the signature isn't near
    /// expiry — to keep a fresh safety margin (e.g. daily). Null = expiry-driven
    /// only.
    /// </summary>
    public TimeSpan? ResignInterval { get; set; }
}
