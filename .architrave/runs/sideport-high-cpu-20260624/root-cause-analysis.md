# Root-Cause Analysis

## Runtime Signal

- `default/sideport-789c7d689f-sqpwz` consumed about one CPU core (`999m`).
- `htop` showed one `.NET TP Worker` near 99% CPU inside `dotnet Sideport.Api.dll`.
- Over 30 minutes the admin/API made about 30 `GET /api/devices/{udid}/installed-apps` calls.
- The same window emitted about 1110 `unexpected provisioning-profile node shape: Invalid type expected Data found Dict` warnings.
- One `installed-apps` request took about 1437 ms.

## Source Path

1. `src/Sideport.Admin/src/api/sideportApi.ts` polls `fetchSnapshot()` every 15 seconds.
2. The snapshot fetches `/api/devices` and then `fetchInstalledApps()` calls `/api/devices/{udid}/installed-apps` for each reachable device with a UDID.
3. `src/Sideport.Api/Program.cs` maps that endpoint to `IDeviceController.ListInstalledAppsAsync`.
4. `NetimobiledeviceController.ListInstalledAppsAsync` always performs both:
   - `_backend.ListInstalledAppsAsync(udid)` (`installation_proxy Browse`), and
   - `_backend.ListProvisioningProfilesAsync(udid)` (`misagent GetInstalledProvisioningProfiles`), then parses profiles and joins expiry to apps.
5. `NetimobiledeviceBackend.ListProvisioningProfilesAsync` expected profile nodes to be `Data`; the live device returned many `Dict` nodes, producing the warning storm.

## Current Hypothesis

The sustained CPU came from repeated polling of an expensive installed-apps read model. The most suspicious sub-path is the provisioning-profile half (`misagent_profiles`) because it produced the warning burst, but `installation_proxy_browse` may also be slow. Observer overhead from Netdata/otel is secondary and amplified the heat, not the primary root cause.

## Remaining Hypotheses

| Hypothesis | Probe Added |
| --- | --- |
| `installation_proxy Browse` is slow | `sideport_device_backend_operation_duration_seconds{operation="installation_proxy_browse",connection_type,result}` |
| `misagent GetInstalledProvisioningProfiles` is slow | `sideport_device_backend_operation_duration_seconds{operation="misagent_profiles",connection_type,result}` |
| Profile node shape mismatch is frequent | `sideport_device_provisioning_profile_shape_warnings_total{node_type}` |
| UI polling sustains the load | `sideport_device_installed_apps_requests_total{result}` plus request duration summaries |
| Large result sets increase CPU | `sideport_device_installed_apps_items_total` and backend operation item totals |

## Metrics Added

- `/metrics` exposes Prometheus text.
- `/metrics` is open through OIDC like `/healthz` and `/readyz`, but the labels deliberately avoid UDID, bundle ID, Apple ID, IP address, and app name.
- Metrics use coarse labels only: result, operation, connection_type, and node_type.

## Follow-Up Fix Candidates

1. Use the new metrics to decide whether browse, profile retrieval, parsing, or logging dominates.
2. Cache/throttle installed-apps snapshots per device once the dominant cost is known.
3. Stop polling `installed-apps` continuously from the admin snapshot if the page does not need fresh inventory.
4. Downgrade/dedupe repeated profile-shape warnings after preserving a metric counter.
