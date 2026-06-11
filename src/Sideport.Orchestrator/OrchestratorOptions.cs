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
}
