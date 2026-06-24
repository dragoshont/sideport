using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Sideport.Devices;

/// <summary>
/// Low-cardinality operational metrics for the device inventory path.
/// </summary>
public sealed class DeviceMetrics
{
    private readonly object _gate = new();
    private readonly Dictionary<string, RequestStats> _installedApps = new(StringComparer.Ordinal);
    private readonly Dictionary<BackendOperationKey, RequestStats> _backendOperations = new();
    private readonly Dictionary<string, long> _profileShapeWarnings = new(StringComparer.Ordinal);

    public DeviceMetricScope TrackInstalledAppsRequest() =>
        new(this, MetricKind.InstalledApps, operation: null, connectionType: null, Stopwatch.StartNew());

    internal DeviceMetricScope TrackBackendOperation(string operation, string connectionType) =>
        new(this, MetricKind.BackendOperation, NormalizeLabelValue(operation), NormalizeConnection(connectionType), Stopwatch.StartNew());

    internal void RecordProvisioningProfileShapeWarning(string nodeType)
    {
        nodeType = NormalizeLabelValue(nodeType);
        lock (_gate)
        {
            _profileShapeWarnings[nodeType] = _profileShapeWarnings.GetValueOrDefault(nodeType) + 1;
        }
    }

    public string ToPrometheusText()
    {
        Dictionary<string, RequestStats> installedApps;
        Dictionary<BackendOperationKey, RequestStats> backendOperations;
        Dictionary<string, long> profileShapeWarnings;

        lock (_gate)
        {
            installedApps = new Dictionary<string, RequestStats>(_installedApps, StringComparer.Ordinal);
            backendOperations = new Dictionary<BackendOperationKey, RequestStats>(_backendOperations);
            profileShapeWarnings = new Dictionary<string, long>(_profileShapeWarnings, StringComparer.Ordinal);
        }

        var text = new StringBuilder();
        AppendInstalledApps(text, installedApps);
        AppendBackendOperations(text, backendOperations);
        AppendProfileShapeWarnings(text, profileShapeWarnings);
        return text.ToString();
    }

    private void RecordInstalledAppsRequest(string result, TimeSpan duration, int itemCount)
    {
        result = NormalizeResult(result);
        lock (_gate)
        {
            RequestStats stats = _installedApps.GetValueOrDefault(result);
            stats.Record(duration, itemCount);
            _installedApps[result] = stats;
        }
    }

    private void RecordBackendOperation(string operation, string connectionType, string result, TimeSpan duration, int itemCount)
    {
        var key = new BackendOperationKey(NormalizeLabelValue(operation), NormalizeConnection(connectionType), NormalizeResult(result));
        lock (_gate)
        {
            RequestStats stats = _backendOperations.GetValueOrDefault(key);
            stats.Record(duration, itemCount);
            _backendOperations[key] = stats;
        }
    }

    private static void AppendInstalledApps(StringBuilder text, Dictionary<string, RequestStats> metrics)
    {
        text.AppendLine("# HELP sideport_device_installed_apps_requests_total Installed-apps API requests by result.");
        text.AppendLine("# TYPE sideport_device_installed_apps_requests_total counter");
        foreach ((string result, RequestStats stats) in metrics.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            string labels = $"result=\"{EscapeLabel(result)}\"";
            text.Append("sideport_device_installed_apps_requests_total{").Append(labels).Append("} ").AppendLine(stats.Count.ToString(CultureInfo.InvariantCulture));
        }

        text.AppendLine("# HELP sideport_device_installed_apps_duration_seconds Installed-apps API request duration in seconds.");
        text.AppendLine("# TYPE sideport_device_installed_apps_duration_seconds summary");
        foreach ((string result, RequestStats stats) in metrics.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            string labels = $"result=\"{EscapeLabel(result)}\"";
            text.Append("sideport_device_installed_apps_duration_seconds_sum{").Append(labels).Append("} ").AppendLine(stats.DurationSeconds.ToString("0.######", CultureInfo.InvariantCulture));
            text.Append("sideport_device_installed_apps_duration_seconds_count{").Append(labels).Append("} ").AppendLine(stats.Count.ToString(CultureInfo.InvariantCulture));
        }

        text.AppendLine("# HELP sideport_device_installed_apps_items_total Installed apps returned by the API.");
        text.AppendLine("# TYPE sideport_device_installed_apps_items_total counter");
        foreach ((string result, RequestStats stats) in metrics.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            string labels = $"result=\"{EscapeLabel(result)}\"";
            text.Append("sideport_device_installed_apps_items_total{").Append(labels).Append("} ").AppendLine(stats.ItemCount.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static void AppendBackendOperations(StringBuilder text, Dictionary<BackendOperationKey, RequestStats> metrics)
    {
        text.AppendLine("# HELP sideport_device_backend_operation_requests_total Device backend operation requests by operation, connection type, and result.");
        text.AppendLine("# TYPE sideport_device_backend_operation_requests_total counter");
        foreach ((BackendOperationKey key, RequestStats stats) in Ordered(metrics))
        {
            string labels = BackendLabels(key);
            text.Append("sideport_device_backend_operation_requests_total{").Append(labels).Append("} ").AppendLine(stats.Count.ToString(CultureInfo.InvariantCulture));
        }

        text.AppendLine("# HELP sideport_device_backend_operation_duration_seconds Device backend operation duration in seconds.");
        text.AppendLine("# TYPE sideport_device_backend_operation_duration_seconds summary");
        foreach ((BackendOperationKey key, RequestStats stats) in Ordered(metrics))
        {
            string labels = BackendLabels(key);
            text.Append("sideport_device_backend_operation_duration_seconds_sum{").Append(labels).Append("} ").AppendLine(stats.DurationSeconds.ToString("0.######", CultureInfo.InvariantCulture));
            text.Append("sideport_device_backend_operation_duration_seconds_count{").Append(labels).Append("} ").AppendLine(stats.Count.ToString(CultureInfo.InvariantCulture));
        }

        text.AppendLine("# HELP sideport_device_backend_operation_items_total Items returned by device backend operations.");
        text.AppendLine("# TYPE sideport_device_backend_operation_items_total counter");
        foreach ((BackendOperationKey key, RequestStats stats) in Ordered(metrics))
        {
            string labels = BackendLabels(key);
            text.Append("sideport_device_backend_operation_items_total{").Append(labels).Append("} ").AppendLine(stats.ItemCount.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static void AppendProfileShapeWarnings(StringBuilder text, Dictionary<string, long> metrics)
    {
        text.AppendLine("# HELP sideport_device_provisioning_profile_shape_warnings_total Provisioning-profile nodes that were not returned as Data by misagent.");
        text.AppendLine("# TYPE sideport_device_provisioning_profile_shape_warnings_total counter");
        foreach ((string nodeType, long count) in metrics.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            text.Append("sideport_device_provisioning_profile_shape_warnings_total{node_type=\"")
                .Append(EscapeLabel(nodeType))
                .Append("\"} ")
                .AppendLine(count.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static IEnumerable<KeyValuePair<BackendOperationKey, RequestStats>> Ordered(Dictionary<BackendOperationKey, RequestStats> metrics) =>
        metrics
            .OrderBy(kv => kv.Key.Operation, StringComparer.Ordinal)
            .ThenBy(kv => kv.Key.ConnectionType, StringComparer.Ordinal)
            .ThenBy(kv => kv.Key.Result, StringComparer.Ordinal);

    private static string BackendLabels(BackendOperationKey key) =>
        $"operation=\"{EscapeLabel(key.Operation)}\",connection_type=\"{EscapeLabel(key.ConnectionType)}\",result=\"{EscapeLabel(key.Result)}\"";

    private static string NormalizeResult(string value) =>
        NormalizeLabelValue(value) switch
        {
            "success" => "success",
            "failure" => "failure",
            "canceled" => "canceled",
            _ => "unknown",
        };

    private static string NormalizeConnection(string value) =>
        NormalizeLabelValue(value) switch
        {
            "usb" => "usb",
            "wifi" => "wifi",
            "network" => "wifi",
            _ => "unknown",
        };

    private static string NormalizeLabelValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "unknown";

        var builder = new StringBuilder(value.Length);
        foreach (char c in value.Trim().ToLowerInvariant())
            builder.Append(char.IsAsciiLetterOrDigit(c) ? c : '_');
        return builder.Length == 0 ? "unknown" : builder.ToString();
    }

    private static string EscapeLabel(string value) =>
        value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);

    internal enum MetricKind { InstalledApps, BackendOperation }

    private readonly record struct BackendOperationKey(string Operation, string ConnectionType, string Result);

    private struct RequestStats
    {
        public long Count { get; private set; }
        public long ItemCount { get; private set; }
        public double DurationSeconds { get; private set; }

        public void Record(TimeSpan duration, int itemCount)
        {
            Count++;
            ItemCount += Math.Max(0, itemCount);
            DurationSeconds += Math.Max(0, duration.TotalSeconds);
        }
    }

    public sealed class DeviceMetricScope : IDisposable
    {
        private readonly DeviceMetrics _owner;
        private readonly MetricKind _kind;
        private readonly string? _operation;
        private readonly string? _connectionType;
        private readonly Stopwatch _stopwatch;
        private int _itemCount;
        private string _result = "failure";

        internal DeviceMetricScope(DeviceMetrics owner, MetricKind kind, string? operation, string? connectionType, Stopwatch stopwatch)
        {
            _owner = owner;
            _kind = kind;
            _operation = operation;
            _connectionType = connectionType;
            _stopwatch = stopwatch;
        }

        public void Succeed(int itemCount)
        {
            _itemCount = itemCount;
            _result = "success";
        }

        public void Cancel() => _result = "canceled";

        public void Dispose()
        {
            if (_kind == MetricKind.InstalledApps)
            {
                _owner.RecordInstalledAppsRequest(_result, _stopwatch.Elapsed, _itemCount);
                return;
            }

            _owner.RecordBackendOperation(_operation!, _connectionType!, _result, _stopwatch.Elapsed, _itemCount);
        }
    }
}