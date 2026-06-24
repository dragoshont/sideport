namespace Sideport.Devices.Tests;

public class DeviceMetricsTests
{
    [Fact]
    public async Task Controller_RecordsInstalledAppsRequestWithoutUdidLabel()
    {
        var backend = new FakeDeviceBackend();
        var metrics = new DeviceMetrics();
        backend.AppsByUdid["UDID-SECRET"] =
        [
            new BackendApp("com.example.app", "App", "1.0", IsUserApp: true),
        ];
        var controller = new NetimobiledeviceController(backend, metrics: metrics);

        await controller.ListInstalledAppsAsync("UDID-SECRET");

        string text = metrics.ToPrometheusText();
        Assert.Contains("sideport_device_installed_apps_requests_total{result=\"success\"} 1", text);
        Assert.Contains("sideport_device_installed_apps_items_total{result=\"success\"} 1", text);
        Assert.DoesNotContain("UDID-SECRET", text);
    }

    [Fact]
    public void ProvisioningProfileShapeWarning_NormalizesNodeType()
    {
        var metrics = new DeviceMetrics();

        metrics.RecordProvisioningProfileShapeWarning("DictionaryNode");
        metrics.RecordProvisioningProfileShapeWarning("DictionaryNode");

        string text = metrics.ToPrometheusText();
        Assert.Contains("sideport_device_provisioning_profile_shape_warnings_total{node_type=\"dictionarynode\"} 2", text);
    }

    [Fact]
    public void BackendOperationMetrics_SeparateOperationAndConnectionType()
    {
        var metrics = new DeviceMetrics();
        using (DeviceMetrics.DeviceMetricScope browse = metrics.TrackBackendOperation("installation_proxy_browse", "Network"))
            browse.Succeed(42);
        using (DeviceMetrics.DeviceMetricScope profiles = metrics.TrackBackendOperation("misagent_profiles", "usb"))
            profiles.Succeed(3);

        string text = metrics.ToPrometheusText();
        Assert.Contains("sideport_device_backend_operation_requests_total{operation=\"installation_proxy_browse\",connection_type=\"wifi\",result=\"success\"} 1", text);
        Assert.Contains("sideport_device_backend_operation_items_total{operation=\"misagent_profiles\",connection_type=\"usb\",result=\"success\"} 3", text);
    }
}