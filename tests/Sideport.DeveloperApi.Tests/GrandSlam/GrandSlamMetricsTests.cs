using Sideport.DeveloperApi.GrandSlam;

namespace Sideport.DeveloperApi.Tests.GrandSlam;

public sealed class GrandSlamMetricsTests
{
    [Fact]
    public void Snapshot_UsesOnlyBoundedOperationAndStatusLabels()
    {
        var metrics = new GrandSlamMetrics();
        metrics.RecordResponse("init", 200);
        metrics.RecordResponse("apptokens", 503);
        metrics.RecordResponse("attacker-controlled-value", 418);
        metrics.RecordRetry("apptokens");

        string text = metrics.ToPrometheusText();

        Assert.Contains(
            "sideport_grandslam_http_responses_total{operation=\"init\",status=\"200\"} 1",
            text,
            StringComparison.Ordinal);
        Assert.Contains(
            "sideport_grandslam_http_responses_total{operation=\"apptokens\",status=\"503\"} 1",
            text,
            StringComparison.Ordinal);
        Assert.Contains(
            "sideport_grandslam_http_responses_total{operation=\"other\",status=\"418\"} 1",
            text,
            StringComparison.Ordinal);
        Assert.Contains(
            "sideport_grandslam_retries_total{operation=\"apptokens\"} 1",
            text,
            StringComparison.Ordinal);
        Assert.DoesNotContain("attacker-controlled-value", text, StringComparison.Ordinal);
    }
}
