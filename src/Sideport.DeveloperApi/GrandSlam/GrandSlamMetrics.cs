using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace Sideport.DeveloperApi.GrandSlam;

/// <summary>Low-cardinality GrandSlam transport metrics.</summary>
public sealed class GrandSlamMetrics
{
    private readonly ConcurrentDictionary<ResponseKey, long> _responses = new();
    private readonly ConcurrentDictionary<string, long> _retries = new(StringComparer.Ordinal);

    internal void RecordResponse(string operation, int statusCode) =>
        _responses.AddOrUpdate(
            new ResponseKey(NormalizeOperation(operation), statusCode),
            1,
            static (_, current) => current + 1);

    internal void RecordRetry(string operation) =>
        _retries.AddOrUpdate(
            NormalizeOperation(operation),
            1,
            static (_, current) => current + 1);

    public string ToPrometheusText()
    {
        var text = new StringBuilder();
        text.AppendLine("# HELP sideport_grandslam_http_responses_total GrandSlam HTTP responses by operation and status.");
        text.AppendLine("# TYPE sideport_grandslam_http_responses_total counter");
        foreach ((ResponseKey key, long count) in _responses.OrderBy(item => item.Key.Operation, StringComparer.Ordinal)
                     .ThenBy(item => item.Key.StatusCode))
        {
            text.Append("sideport_grandslam_http_responses_total{operation=\"")
                .Append(key.Operation)
                .Append("\",status=\"")
                .Append(key.StatusCode.ToString(CultureInfo.InvariantCulture))
                .Append("\"} ")
                .AppendLine(count.ToString(CultureInfo.InvariantCulture));
        }

        text.AppendLine("# HELP sideport_grandslam_retries_total GrandSlam retries by operation.");
        text.AppendLine("# TYPE sideport_grandslam_retries_total counter");
        foreach ((string operation, long count) in _retries.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            text.Append("sideport_grandslam_retries_total{operation=\"")
                .Append(operation)
                .Append("\"} ")
                .AppendLine(count.ToString(CultureInfo.InvariantCulture));
        }
        return text.ToString();
    }

    private static string NormalizeOperation(string operation) => operation switch
    {
        "init" => "init",
        "complete" => "complete",
        "apptokens" => "apptokens",
        _ => "other",
    };

    private readonly record struct ResponseKey(string Operation, int StatusCode);
}
