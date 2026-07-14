using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sideport.Api.DeviceInventory;
using Sideport.Api.Operations;
using Sideport.Core;
using Sideport.Orchestrator;

namespace Sideport.Api.Tests;

public sealed class DeviceEnrollmentTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"sideport-enrollment-{Guid.NewGuid():N}");

    public DeviceEnrollmentTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task KnownDeviceStore_LegacyJsonLoadsAsUnverified()
    {
        string path = Path.Combine(_directory, "known-devices.json");
        await File.WriteAllTextAsync(path, """
            [
              {
                "udid": "legacy-udid",
                "displayName": "Legacy iPhone",
                "connection": "usb",
                "firstSeenAt": "2026-07-01T10:00:00Z",
                "lastSeenSource": "live-poll",
                "trustState": "trusted",
                "owner": "Legacy family label",
                "updatedAt": "2026-07-01T10:00:00Z"
              }
            ]
            """);

        var store = new KnownDeviceStore(path);

        KnownDeviceRecord record = Assert.Single(await store.ListAsync());
        Assert.Equal("legacy-unverified", record.InventoryState);
        Assert.Null(record.AcceptedAt);
        Assert.Null(record.AcceptedBy);
        Assert.Null(record.EnrollmentOperationId);
        Assert.Null(record.OwnerMemberId);
    }

    [Fact]
    public async Task ManualUpsert_OfReachableTrustedDevice_NeverAcceptsIt()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var controller = new FakeDeviceController
        {
            Devices =
            [
                new DeviceInfo("manual-udid", "My iPhone", "iPhone15,2", "18.5", DeviceConnection.Usb,
                    "trusted", "Lockdown verified.", now, true),
            ],
        };
        var store = new KnownDeviceStore(Path.Combine(_directory, "known-devices.json"));
        var service = new KnownDeviceService(store, controller, new InMemoryAppRegistry());

        (KnownDeviceDto device, _) = await service.UpsertAsync(new KnownDeviceUpsertRequest("manual-udid"));

        Assert.Equal("discovered", device.InventoryState);
        Assert.Null(device.AcceptedAt);
        Assert.Null(device.EnrollmentOperationId);
        Assert.True(device.SupportedForFirstInstall);
    }

    [Fact]
    public async Task ManualUpsert_WhenDeviceTransportIsUnavailable_PersistsOfflineInventory()
    {
        var controller = new FakeDeviceController
        {
            ListHandler = _ => throw new InvalidOperationException("usbmux unavailable"),
        };
        var store = new KnownDeviceStore(Path.Combine(_directory, "known-devices-offline.json"));
        var service = new KnownDeviceService(store, controller, new InMemoryAppRegistry());

        (KnownDeviceDto device, bool created) = await service.UpsertAsync(
            new KnownDeviceUpsertRequest("offline-manual-udid", "Offline iPhone"));

        Assert.True(created);
        Assert.Equal("discovered", device.InventoryState);
        Assert.Equal("offline", device.Connection);
        Assert.False(device.UsableForInstall);
        Assert.False(device.SupportedForFirstInstall);
    }

    [Fact]
    public async Task AlreadyTrustedUsbDevice_IsAcceptedWithoutPairing()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var controller = FakeDeviceController.WithSingleUsb("trusted-udid", "trusted", now);
        await using EnrollmentFixture fixture = CreateFixture(controller);

        DeviceEnrollmentSubmissionResult submitted = await fixture.Service.StartAsync(
            new DeviceEnrollmentRequest("trusted-key"),
            new OperationActorDto("api-token", "api-token-client"));
        await fixture.Service.ProcessAsync(submitted.Record!.OperationId);

        OperationRecordDto completed = (await fixture.Operations.FindAsync(submitted.Record.OperationId))!;
        KnownDeviceRecord accepted = Assert.Single(await fixture.KnownDevices.ListAsync());
        Assert.Equal("succeeded", completed.Status);
        Assert.Equal("accepted", accepted.InventoryState);
        Assert.Equal(completed.OperationId, accepted.EnrollmentOperationId);
        Assert.Equal(0, controller.PairCalls);
        Assert.True(controller.ProbeCalls >= 2);
    }

    [Fact]
    public async Task ExplicitMemberSnapshots_FlowFromEnrollmentToAcceptedDeviceAndDto()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var controller = FakeDeviceController.WithSingleUsb("family-udid", "trusted", now);
        await using EnrollmentFixture fixture = CreateFixture(controller);

        DeviceEnrollmentSubmissionResult submitted = await fixture.Service.StartAsync(
            new DeviceEnrollmentRequest("family-key", TargetMemberId: " member_family "),
            new OperationActorDto("oidc-user", "Family member", "principal-hash"),
            actorMemberId: " member_family ");
        await fixture.Service.ProcessAsync(submitted.Record!.OperationId);

        OperationRecordDto completed = (await fixture.Operations.FindAsync(submitted.Record.OperationId))!;
        KnownDeviceRecord accepted = Assert.Single(await fixture.KnownDevices.ListAsync());
        KnownDeviceDto dto = Assert.Single(await fixture.Inventory.ListAsync(includeReachable: false));
        Assert.Equal("member_family", completed.ActorMemberId);
        Assert.Equal("member_family", completed.OwnerMemberId);
        Assert.Equal("member_family", accepted.OwnerMemberId);
        Assert.Equal("member_family", dto.OwnerMemberId);
    }

    [Fact]
    public async Task AcceptedDevice_CannotBeReassignedByAnotherEnrollment()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var controller = FakeDeviceController.WithSingleUsb("owned-udid", "trusted", now);
        await using EnrollmentFixture fixture = CreateFixture(controller);
        DeviceEnrollmentSubmissionResult submitted = await fixture.Service.StartAsync(
            new DeviceEnrollmentRequest("owned-key", TargetMemberId: "member_one"),
            new OperationActorDto("oidc-user", "Owner", "principal-hash"),
            actorMemberId: "member_owner");
        await fixture.Service.ProcessAsync(submitted.Record!.OperationId);

        DeviceInfo current = Assert.Single(controller.Devices);
        var trust = new DeviceTrustProbe(
            current.Udid,
            DeviceConnection.Usb,
            "trusted",
            "Lockdown verified.",
            DateTimeOffset.UtcNow,
            true);
        KnownDeviceAcceptanceException error = await Assert.ThrowsAsync<KnownDeviceAcceptanceException>(() =>
            fixture.Inventory.AcceptAsync(
                current,
                trust,
                "Other owner",
                "op_other_enrollment",
                "member_two"));

        Assert.Equal("device-already-accepted", error.Code);
        KnownDeviceRecord stillOwned = Assert.Single(await fixture.KnownDevices.ListAsync());
        Assert.Equal("member_one", stillOwned.OwnerMemberId);
    }

    [Fact]
    public async Task EnrollmentIdempotency_RejectsDifferentMemberTarget()
    {
        var controller = new FakeDeviceController();
        await using EnrollmentFixture fixture = CreateFixture(controller);
        var actor = new OperationActorDto("oidc-user", "Owner", "principal-hash");

        DeviceEnrollmentSubmissionResult first = await fixture.Service.StartAsync(
            new DeviceEnrollmentRequest("member-key", TargetMemberId: "member_one"),
            actor,
            actorMemberId: "member_owner");
        DeviceEnrollmentSubmissionResult conflict = await fixture.Service.StartAsync(
            new DeviceEnrollmentRequest("member-key", TargetMemberId: "member_two"),
            actor,
            actorMemberId: "member_owner");

        Assert.True(first.Created);
        Assert.False(conflict.Created);
        Assert.Equal("idempotency-member-conflict", conflict.Error);
        Assert.Equal(first.Record?.OperationId, conflict.Record?.OperationId);
    }

    [Fact]
    public async Task EnrollmentRetry_PreservesTargetOwnerAndSnapshotsCurrentActor()
    {
        var controller = new FakeDeviceController();
        await using EnrollmentFixture fixture = CreateFixture(controller);
        var actor = new OperationActorDto("oidc-user", "Owner", "principal-hash");
        DeviceEnrollmentSubmissionResult submitted = await fixture.Service.StartAsync(
            new DeviceEnrollmentRequest("source-key", TargetMemberId: "member_family"),
            actor,
            actorMemberId: "member_owner");
        await fixture.Operations.TransitionAsync(submitted.Record!.OperationId, record => record with
        {
            Status = "failed",
            CompletedAt = DateTimeOffset.UtcNow,
            Retryable = true,
            Cancelable = false,
        });

        DeviceEnrollmentSubmissionResult retry = await fixture.Service.RetryAsync(
            submitted.Record.OperationId,
            actor,
            "retry-key",
            actorMemberId: "member_owner");

        Assert.True(retry.Created);
        Assert.Equal("member_owner", retry.Record?.ActorMemberId);
        Assert.Equal("member_family", retry.Record?.OwnerMemberId);
    }

    [Fact]
    public async Task MultipleUsbCandidates_BlockWithSafeSummariesBeforePairing()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var controller = new FakeDeviceController
        {
            Devices =
            [
                TrustedDevice("00008110-AAAA1111", "Personal iPhone", now),
                TrustedDevice("00008110-BBBB2222", "Lab iPhone", now),
            ],
        };
        await using EnrollmentFixture fixture = CreateFixture(controller);

        DeviceEnrollmentSubmissionResult submitted = await fixture.Service.StartAsync(
            new DeviceEnrollmentRequest("selection-key"),
            new OperationActorDto("oidc-user", "owner@example.test"));
        await fixture.Service.ProcessAsync(submitted.Record!.OperationId);

        OperationRecordDto blocked = (await fixture.Operations.FindAsync(submitted.Record.OperationId))!;
        Assert.Equal("blocked", blocked.Status);
        Assert.Equal("device-selection-required", blocked.Error?.Code);
        Assert.Equal(2, blocked.CandidateDevices?.Count);
        Assert.All(blocked.CandidateDevices!, candidate => Assert.Equal(8, candidate.UdidSuffix.Length));
        string json = JsonSerializer.Serialize(blocked, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("00008110-AAAA1111", json, StringComparison.Ordinal);
        Assert.DoesNotContain("00008110-BBBB2222", json, StringComparison.Ordinal);
        Assert.Equal(0, controller.PairCalls);
        Assert.Empty(await fixture.KnownDevices.ListAsync());
    }

    [Fact]
    public async Task PairingTimeout_IsRecoveryRequiredAndAddsNothing()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var controller = FakeDeviceController.WithSingleUsb("timeout-udid", "untrusted", now);
        controller.PairHandler = async (_, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new InvalidOperationException("unreachable");
        };
        await using EnrollmentFixture fixture = CreateFixture(
            controller,
            new DeviceEnrollmentOptions { SessionTimeout = TimeSpan.FromMilliseconds(80), PollInterval = TimeSpan.FromMilliseconds(5) });

        DeviceEnrollmentSubmissionResult submitted = await fixture.Service.StartAsync(
            new DeviceEnrollmentRequest("timeout-key", "timeout-udid"),
            new OperationActorDto("api-token", "api-token-client"));
        await fixture.Service.ProcessAsync(submitted.Record!.OperationId);

        OperationRecordDto failed = (await fixture.Operations.FindAsync(submitted.Record.OperationId))!;
        Assert.Equal("recovery-required", failed.Status);
        Assert.Equal("device-enrollment-recovery-required", failed.Error?.Code);
        Assert.NotNull(failed.DevicePairingRequestedAt);
        Assert.Equal(1, controller.PairCalls);
        Assert.Empty(await fixture.KnownDevices.ListAsync());
    }

    [Fact]
    public async Task ExpiredRestartAfterPairRequest_RemainsRecoveryRequired()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var controller = FakeDeviceController.WithSingleUsb("expired-pair-udid", "untrusted", now);
        await using EnrollmentFixture fixture = CreateFixture(
            controller,
            new DeviceEnrollmentOptions { SessionTimeout = TimeSpan.FromMilliseconds(40), PollInterval = TimeSpan.FromMilliseconds(5) });

        DeviceEnrollmentSubmissionResult submitted = await fixture.Service.StartAsync(
            new DeviceEnrollmentRequest("expired-pair-key", "expired-pair-udid"),
            new OperationActorDto("api-token", "api-token-client"));
        DateTimeOffset pairingRequestedAt = DateTimeOffset.UtcNow.AddHours(-1);
        await fixture.Operations.TransitionAsync(submitted.Record!.OperationId, record => record with
        {
            Status = "running",
            UpdatedAt = pairingRequestedAt,
            DevicePairingRequestedAt = pairingRequestedAt,
            Cancelable = false,
        });
        await Task.Delay(80);

        await fixture.Service.ProcessAsync(submitted.Record.OperationId);

        OperationRecordDto recovered = (await fixture.Operations.FindAsync(submitted.Record.OperationId))!;
        Assert.Equal("recovery-required", recovered.Status);
        Assert.Equal("device-enrollment-recovery-required", recovered.Error?.Code);
        Assert.Equal(0, controller.PairCalls);
        Assert.Empty(await fixture.KnownDevices.ListAsync());
    }

    [Fact]
    public async Task SynchronousTransportOpen_CannotHoldEnrollmentPastDeadline()
    {
        using var releaseTransport = new ManualResetEventSlim(false);
        var transportEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var controller = new FakeDeviceController
        {
            ListHandler = _ =>
            {
                transportEntered.TrySetResult();
                releaseTransport.Wait();
                return Task.FromResult<IReadOnlyList<DeviceInfo>>([]);
            },
        };
        await using EnrollmentFixture fixture = CreateFixture(
            controller,
            new DeviceEnrollmentOptions { SessionTimeout = TimeSpan.FromMilliseconds(500), PollInterval = TimeSpan.FromMilliseconds(5) });
        DeviceEnrollmentSubmissionResult submitted = await fixture.Service.StartAsync(
            new DeviceEnrollmentRequest("bounded-sync-open"),
            new OperationActorDto("api-token", "api-token-client"));

        Task processing = Task.Run(() => fixture.Service.ProcessAsync(submitted.Record!.OperationId));
        await transportEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(750);
        bool completedWithinBound = processing.IsCompleted;
        releaseTransport.Set();
        await processing.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(completedWithinBound);
        OperationRecordDto failed = (await fixture.Operations.FindAsync(submitted.Record!.OperationId))!;
        Assert.Equal("failed", failed.Status);
        Assert.Equal("device-enrollment-timeout", failed.Error?.Code);
        Assert.Equal(0, controller.PairCalls);
    }

    [Fact]
    public async Task Worker_RequeuesActiveEnrollmentAfterTransientStoreFailure()
    {
        string operationsPath = Path.Combine(_directory, "worker-operations.json");
        var operations = new OperationStore(operationsPath);
        var knownDevices = new KnownDeviceStore(Path.Combine(_directory, "worker-known.json"));
        var controller = new FakeDeviceController();
        var inventory = new KnownDeviceService(knownDevices, controller, new InMemoryAppRegistry());
        var queue = new DeviceEnrollmentQueue();
        var service = new DeviceEnrollmentService(
            operations,
            knownDevices,
            inventory,
            controller,
            queue,
            new DeviceEnrollmentOptions { SessionTimeout = TimeSpan.FromMilliseconds(75), PollInterval = TimeSpan.FromMilliseconds(5) });
        DeviceEnrollmentSubmissionResult submitted = await service.StartAsync(
            new DeviceEnrollmentRequest("worker-requeue"),
            new OperationActorDto("api-token", "api-token-client"));

        File.Delete(operationsPath);
        Directory.CreateDirectory(operationsPath);
        var logger = new EnrollmentWorkerLogger();
        var worker = new DeviceEnrollmentWorker(queue, service, logger);
        using var stopping = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await worker.StartAsync(stopping.Token);
        await logger.ErrorLogged.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Directory.Delete(operationsPath);

        OperationRecordDto? terminal = null;
        for (int i = 0; i < 150; i++)
        {
            terminal = await operations.FindAsync(submitted.Record!.OperationId);
            if (terminal?.Status is "failed" or "recovery-required")
                break;
            await Task.Delay(20);
        }
        await worker.StopAsync(CancellationToken.None);

        Assert.NotNull(terminal);
        Assert.Equal("failed", terminal!.Status);
        Assert.Equal("device-enrollment-timeout", terminal.Error?.Code);
        Assert.True(logger.ErrorCount >= 1);
    }

    [Fact]
    public async Task UnknownVerificationAfterPairing_RetriesTrustAutomaticallyAndAccepts()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var controller = FakeDeviceController.WithSingleUsb("ambiguous-udid", "untrusted", now);
        controller.Probes.Enqueue(new DeviceTrustProbe(
            "ambiguous-udid", DeviceConnection.Usb, "untrusted", "Trust is not established.", now, false));
        controller.Probes.Enqueue(new DeviceTrustProbe(
            "ambiguous-udid", DeviceConnection.Usb, "unknown", "The final lockdown reply was lost.", now, false));
        controller.Probes.Enqueue(new DeviceTrustProbe(
            "ambiguous-udid", DeviceConnection.Usb, "trusted", "Lockdown verified.", now, true));
        controller.PairHandler = (udid, _) => Task.FromResult(new DevicePairingResult(
            udid, DeviceConnection.Usb, "trusted", "Trust accepted.", DateTimeOffset.UtcNow, true));
        await using EnrollmentFixture fixture = CreateFixture(controller);

        DeviceEnrollmentSubmissionResult submitted = await fixture.Service.StartAsync(
            new DeviceEnrollmentRequest("ambiguous-key", "ambiguous-udid"),
            new OperationActorDto("api-token", "api-token-client"));
        await fixture.Service.ProcessAsync(submitted.Record!.OperationId);

        OperationRecordDto completed = (await fixture.Operations.FindAsync(submitted.Record.OperationId))!;
        Assert.True(
            string.Equals(completed.Status, "succeeded", StringComparison.Ordinal),
            $"Expected success, got {completed.Status}: {completed.Error?.Code} {completed.Error?.Message}");
        Assert.Equal(1, controller.PairCalls);
        Assert.True(controller.ProbeCalls >= 3);
        Assert.Equal("accepted", Assert.Single(await fixture.KnownDevices.ListAsync()).InventoryState);
    }

    [Fact]
    public async Task UntrustedProbeAfterPairing_WaitsForTrustAndAcceptsWithoutPairingAgain()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var controller = FakeDeviceController.WithSingleUsb("pending-trust-udid", "untrusted", now);
        controller.Probes.Enqueue(new DeviceTrustProbe(
            "pending-trust-udid", DeviceConnection.Usb, "untrusted", "Trust is not established.", now, false));
        controller.Probes.Enqueue(new DeviceTrustProbe(
            "pending-trust-udid", DeviceConnection.Usb, "untrusted", "No valid pairing record is available for this iPhone.", now, false));
        controller.Probes.Enqueue(new DeviceTrustProbe(
            "pending-trust-udid", DeviceConnection.Usb, "trusted", "Lockdown verified.", now, true));
        controller.PairHandler = (udid, _) => Task.FromResult(new DevicePairingResult(
            udid, DeviceConnection.Usb, "unknown", "Waiting for Trust.", DateTimeOffset.UtcNow, false));
        await using EnrollmentFixture fixture = CreateFixture(
            controller,
            new DeviceEnrollmentOptions { SessionTimeout = TimeSpan.FromMilliseconds(500), PollInterval = TimeSpan.FromMilliseconds(5) });

        DeviceEnrollmentSubmissionResult submitted = await fixture.Service.StartAsync(
            new DeviceEnrollmentRequest("pending-trust-key", "pending-trust-udid"),
            new OperationActorDto("api-token", "api-token-client"));
        await fixture.Service.ProcessAsync(submitted.Record!.OperationId);

        OperationRecordDto completed = (await fixture.Operations.FindAsync(submitted.Record.OperationId))!;
        Assert.Equal("succeeded", completed.Status);
        Assert.NotNull(completed.DevicePairingRequestedAt);
        Assert.Equal(1, controller.PairCalls);
        Assert.True(controller.ProbeCalls >= 3);
        Assert.Equal("accepted", Assert.Single(await fixture.KnownDevices.ListAsync()).InventoryState);
    }

    [Fact]
    public async Task UntrustedPairResult_WaitsForTrustAndAcceptsWithoutPairingAgain()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var controller = FakeDeviceController.WithSingleUsb("pair-result-pending-udid", "untrusted", now);
        controller.Probes.Enqueue(new DeviceTrustProbe(
            "pair-result-pending-udid", DeviceConnection.Usb, "untrusted", "Trust is not established.", now, false));
        controller.Probes.Enqueue(new DeviceTrustProbe(
            "pair-result-pending-udid", DeviceConnection.Usb, "trusted", "Lockdown verified.", now, true));
        controller.PairHandler = (udid, _) => Task.FromResult(new DevicePairingResult(
            udid, DeviceConnection.Usb, "untrusted", "No valid pairing record is available yet.", DateTimeOffset.UtcNow, false));
        await using EnrollmentFixture fixture = CreateFixture(
            controller,
            new DeviceEnrollmentOptions { SessionTimeout = TimeSpan.FromMilliseconds(500), PollInterval = TimeSpan.FromMilliseconds(5) });

        DeviceEnrollmentSubmissionResult submitted = await fixture.Service.StartAsync(
            new DeviceEnrollmentRequest("pair-result-pending-key", "pair-result-pending-udid"),
            new OperationActorDto("api-token", "api-token-client"));
        await fixture.Service.ProcessAsync(submitted.Record!.OperationId);

        OperationRecordDto completed = (await fixture.Operations.FindAsync(submitted.Record.OperationId))!;
        Assert.Equal("succeeded", completed.Status);
        Assert.NotNull(completed.DevicePairingRequestedAt);
        Assert.Equal(1, controller.PairCalls);
        Assert.Equal("accepted", Assert.Single(await fixture.KnownDevices.ListAsync()).InventoryState);
    }

    [Fact]
    public async Task ExplicitTrustDenialPairResult_FailsWithoutRepeatingPairing()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var controller = FakeDeviceController.WithSingleUsb("pair-result-denied-udid", "untrusted", now);
        controller.Probes.Enqueue(new DeviceTrustProbe(
            "pair-result-denied-udid", DeviceConnection.Usb, "untrusted", "Trust is not established.", now, false));
        controller.PairHandler = (udid, _) => Task.FromResult(new DevicePairingResult(
            udid, DeviceConnection.Usb, "untrusted", "Trust was denied on the iPhone.", DateTimeOffset.UtcNow, false));
        await using EnrollmentFixture fixture = CreateFixture(
            controller,
            new DeviceEnrollmentOptions { SessionTimeout = TimeSpan.FromMilliseconds(500), PollInterval = TimeSpan.FromMilliseconds(5) });

        DeviceEnrollmentSubmissionResult submitted = await fixture.Service.StartAsync(
            new DeviceEnrollmentRequest("pair-result-denied-key", "pair-result-denied-udid"),
            new OperationActorDto("api-token", "api-token-client"));
        await fixture.Service.ProcessAsync(submitted.Record!.OperationId);

        OperationRecordDto failed = (await fixture.Operations.FindAsync(submitted.Record.OperationId))!;
        Assert.Equal("failed", failed.Status);
        Assert.Equal("device-lockdown-untrusted", failed.Error?.Code);
        Assert.Equal(1, controller.PairCalls);
        Assert.Empty(await fixture.KnownDevices.ListAsync());
    }

    [Fact]
    public async Task ExplicitTrustDenialAfterPairing_FailsWithoutRepeatingPairing()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var controller = FakeDeviceController.WithSingleUsb("denied-trust-udid", "untrusted", now);
        controller.Probes.Enqueue(new DeviceTrustProbe(
            "denied-trust-udid", DeviceConnection.Usb, "untrusted", "Trust is not established.", now, false));
        controller.Probes.Enqueue(new DeviceTrustProbe(
            "denied-trust-udid", DeviceConnection.Usb, "untrusted", "Trust was declined on the iPhone.", now, false));
        controller.PairHandler = (udid, _) => Task.FromResult(new DevicePairingResult(
            udid, DeviceConnection.Usb, "unknown", "Waiting for Trust.", DateTimeOffset.UtcNow, false));
        await using EnrollmentFixture fixture = CreateFixture(
            controller,
            new DeviceEnrollmentOptions { SessionTimeout = TimeSpan.FromMilliseconds(500), PollInterval = TimeSpan.FromMilliseconds(5) });

        DeviceEnrollmentSubmissionResult submitted = await fixture.Service.StartAsync(
            new DeviceEnrollmentRequest("denied-trust-key", "denied-trust-udid"),
            new OperationActorDto("api-token", "api-token-client"));
        await fixture.Service.ProcessAsync(submitted.Record!.OperationId);

        OperationRecordDto failed = (await fixture.Operations.FindAsync(submitted.Record.OperationId))!;
        Assert.Equal("failed", failed.Status);
        Assert.Equal("device-lockdown-untrusted", failed.Error?.Code);
        Assert.Equal(1, controller.PairCalls);
        Assert.Empty(await fixture.KnownDevices.ListAsync());
    }

    [Fact]
    public async Task NonUsbResultAfterPairRequest_RecoversWithoutPairingAgain()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var controller = FakeDeviceController.WithSingleUsb("transport-changed", "untrusted", now);
        controller.Probes.Enqueue(new DeviceTrustProbe(
            "transport-changed", DeviceConnection.Usb, "untrusted", "Trust is not established.", now, false));
        controller.Probes.Enqueue(new DeviceTrustProbe(
            "transport-changed", DeviceConnection.Usb, "trusted", "Lockdown verified after reconnect.", now, true));
        controller.PairHandler = (udid, _) => Task.FromResult(new DevicePairingResult(
            udid,
            DeviceConnection.Wifi,
            "trusted",
            "Trust may have completed while the transport changed.",
            DateTimeOffset.UtcNow,
            true));
        await using EnrollmentFixture fixture = CreateFixture(controller);

        DeviceEnrollmentSubmissionResult submitted = await fixture.Service.StartAsync(
            new DeviceEnrollmentRequest("transport-changed-key", "transport-changed"),
            new OperationActorDto("api-token", "api-token-client"));
        await fixture.Service.ProcessAsync(submitted.Record!.OperationId);

        OperationRecordDto recovered = (await fixture.Operations.FindAsync(submitted.Record.OperationId))!;
        Assert.Equal("succeeded", recovered.Status);
        Assert.Equal(1, controller.PairCalls);
        Assert.Equal("accepted", Assert.Single(await fixture.KnownDevices.ListAsync()).InventoryState);
    }

    [Fact]
    public async Task UsbDisconnectAfterPairRequest_WaitsForReconnectAndAcceptsAutomatically()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DeviceInfo device = FakeDeviceController.WithSingleUsb("reconnect-udid", "untrusted", now).Devices.Single();
        int listCalls = 0;
        var controller = new FakeDeviceController
        {
            Devices = [device],
            // StartAsync eligibility is call 1, ProcessAsync USB discovery is
            // call 2, and the first post-pairing recovery read is call 3.
            ListHandler = _ => Task.FromResult<IReadOnlyList<DeviceInfo>>(
                Interlocked.Increment(ref listCalls) == 3 ? [] : [device]),
        };
        controller.Probes.Enqueue(new DeviceTrustProbe(
            device.Udid, DeviceConnection.Usb, "untrusted", "Trust is not established.", now, false));
        controller.Probes.Enqueue(new DeviceTrustProbe(
            device.Udid, DeviceConnection.Usb, "trusted", "Lockdown verified after reconnect.", now, true));
        controller.PairHandler = (udid, _) => Task.FromResult(new DevicePairingResult(
            udid, DeviceConnection.Usb, "unknown", "The USB connection changed after Trust.", DateTimeOffset.UtcNow, false));
        await using EnrollmentFixture fixture = CreateFixture(
            controller,
            new DeviceEnrollmentOptions { SessionTimeout = TimeSpan.FromMilliseconds(500), PollInterval = TimeSpan.FromMilliseconds(5) });

        DeviceEnrollmentSubmissionResult submitted = await fixture.Service.StartAsync(
            new DeviceEnrollmentRequest("reconnect-key", device.Udid),
            new OperationActorDto("api-token", "api-token-client"));
        await fixture.Service.ProcessAsync(submitted.Record!.OperationId);

        OperationRecordDto completed = (await fixture.Operations.FindAsync(submitted.Record.OperationId))!;
        Assert.True(
            string.Equals(completed.Status, "succeeded", StringComparison.Ordinal),
            $"Expected success, got {completed.Status}: {completed.Error?.Code} {completed.Error?.Message}");
        Assert.Equal(1, controller.PairCalls);
        Assert.True(listCalls >= 4);
        Assert.Equal("accepted", Assert.Single(await fixture.KnownDevices.ListAsync()).InventoryState);
    }

    [Fact]
    public async Task FinalVerificationTimeoutAfterPairRequest_IsRecoveryRequired()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var controller = FakeDeviceController.WithSingleUsb("verify-timeout", "untrusted", now);
        int probe = 0;
        controller.ProbeHandler = async (udid, ct) =>
        {
            if (Interlocked.Increment(ref probe) == 1)
                return new DeviceTrustProbe(udid, DeviceConnection.Usb, "untrusted", "Trust is not established.", DateTimeOffset.UtcNow, false);
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new InvalidOperationException("unreachable");
        };
        await using EnrollmentFixture fixture = CreateFixture(
            controller,
            new DeviceEnrollmentOptions { SessionTimeout = TimeSpan.FromMilliseconds(80), PollInterval = TimeSpan.FromMilliseconds(5) });

        DeviceEnrollmentSubmissionResult submitted = await fixture.Service.StartAsync(
            new DeviceEnrollmentRequest("verify-timeout-key", "verify-timeout"),
            new OperationActorDto("api-token", "api-token-client"));
        await fixture.Service.ProcessAsync(submitted.Record!.OperationId);

        OperationRecordDto recovered = (await fixture.Operations.FindAsync(submitted.Record.OperationId))!;
        Assert.Equal("recovery-required", recovered.Status);
        Assert.Equal("device-enrollment-recovery-required", recovered.Error?.Code);
        Assert.Equal(1, controller.PairCalls);
        Assert.Equal("succeeded", recovered.Stages.Single(stage => stage.Id == "request-pairing").Status);
        Assert.Equal("succeeded", recovered.Stages.Single(stage => stage.Id == "await-user-trust").Status);
        Assert.Equal("failed", recovered.Stages.Single(stage => stage.Id == "verify-lockdown").Status);
        Assert.Empty(await fixture.KnownDevices.ListAsync());
    }

    [Fact]
    public async Task IdempotencyReplayUsesActorAndKeyAfterDeviceSelection()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var controller = FakeDeviceController.WithSingleUsb("idempotent-udid", "trusted", now);
        await using EnrollmentFixture fixture = CreateFixture(controller);
        var actor = new OperationActorDto("oidc-user", "owner@example.test");

        DeviceEnrollmentSubmissionResult first = await fixture.Service.StartAsync(new DeviceEnrollmentRequest("same-key"), actor);
        await fixture.Service.ProcessAsync(first.Record!.OperationId);
        DeviceEnrollmentSubmissionResult replay = await fixture.Service.StartAsync(new DeviceEnrollmentRequest("same-key"), actor);

        Assert.False(replay.Created);
        Assert.Null(replay.Error);
        Assert.Equal(first.Record.OperationId, replay.Record?.OperationId);
    }

    private EnrollmentFixture CreateFixture(FakeDeviceController controller, DeviceEnrollmentOptions? options = null)
    {
        var operations = new OperationStore(Path.Combine(_directory, $"operations-{Guid.NewGuid():N}.json"));
        var knownDevices = new KnownDeviceStore(Path.Combine(_directory, $"known-{Guid.NewGuid():N}.json"));
        var inventory = new KnownDeviceService(knownDevices, controller, new InMemoryAppRegistry());
        var queue = new DeviceEnrollmentQueue();
        var service = new DeviceEnrollmentService(operations, knownDevices, inventory, controller, queue, options);
        return new EnrollmentFixture(service, operations, knownDevices, inventory);
    }

    private static DeviceInfo TrustedDevice(string udid, string name, DateTimeOffset now) =>
        new(udid, name, "iPhone15,2", "18.5", DeviceConnection.Usb, "trusted", "Lockdown verified.", now, true);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private sealed record EnrollmentFixture(
        DeviceEnrollmentService Service,
        OperationStore Operations,
        KnownDeviceStore KnownDevices,
        KnownDeviceService Inventory) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeDeviceController : IDeviceController
    {
        public IReadOnlyList<DeviceInfo> Devices { get; set; } = [];
        public Func<CancellationToken, Task<IReadOnlyList<DeviceInfo>>>? ListHandler { get; set; }
        public Queue<DeviceTrustProbe> Probes { get; } = new();
        public Func<string, CancellationToken, Task<DeviceTrustProbe>>? ProbeHandler { get; set; }
        public Func<string, CancellationToken, Task<DevicePairingResult>>? PairHandler { get; set; }
        public int ProbeCalls { get; private set; }
        public int PairCalls { get; private set; }

        public static FakeDeviceController WithSingleUsb(string udid, string trustState, DateTimeOffset now)
        {
            bool trusted = trustState == "trusted";
            return new FakeDeviceController
            {
                Devices =
                [
                    new DeviceInfo(udid, "Test iPhone", "iPhone15,2", "18.5", DeviceConnection.Usb,
                        trustState, trusted ? "Lockdown verified." : "Trust is not established.", now, trusted),
                ],
            };
        }

        public Task<IReadOnlyList<DeviceInfo>> ListDevicesAsync(CancellationToken ct = default) =>
            ListHandler is null ? Task.FromResult(Devices) : ListHandler(ct);

        public Task<DeviceTrustProbe> ProbeTrustAsync(string udid, CancellationToken ct = default)
        {
            ProbeCalls++;
            if (ProbeHandler is not null)
                return ProbeHandler(udid, ct);
            if (Probes.Count > 0)
                return Task.FromResult(Probes.Dequeue());
            DeviceInfo device = Devices.Single(item => string.Equals(item.Udid, udid, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(new DeviceTrustProbe(
                device.Udid,
                device.Connection,
                device.TrustState,
                device.TrustReason,
                DateTimeOffset.UtcNow,
                device.UsableForInstall));
        }

        public Task<DevicePairingResult> PairAsync(
            string udid,
            IProgress<DevicePairingProgress>? progress = null,
            CancellationToken ct = default)
        {
            PairCalls++;
            if (PairHandler is not null)
                return PairHandler(udid, ct);
            return Task.FromResult(new DevicePairingResult(
                udid,
                DeviceConnection.Usb,
                "trusted",
                "Trust accepted.",
                DateTimeOffset.UtcNow,
                true));
        }

        public Task<IReadOnlyList<InstalledApp>> ListInstalledAppsAsync(string udid, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<InstalledApp>>([]);

        public Task InstallAsync(string udid, string ipaPath, CancellationToken ct = default) => Task.CompletedTask;

        public Task<DeviceDiagnostics> DiagnoseAsync(CancellationToken ct = default) =>
            Task.FromResult(new DeviceDiagnostics("ok", []));
    }

    private sealed class EnrollmentWorkerLogger : ILogger<DeviceEnrollmentWorker>
    {
        private int _errorCount;
        public int ErrorCount => Volatile.Read(ref _errorCount);
        public TaskCompletionSource<bool> ErrorLogged { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel < LogLevel.Error)
                return;
            Interlocked.Increment(ref _errorCount);
            ErrorLogged.TrySetResult(true);
        }
    }
}
