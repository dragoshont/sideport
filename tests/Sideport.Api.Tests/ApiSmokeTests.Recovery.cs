using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Sideport.Api.DeviceInventory;
using Sideport.Core;
using Sideport.DeveloperApi.Packaging;
using Sideport.Orchestrator;
using Operations = Sideport.Api.Operations;
using RecoveryRecord = Sideport.Api.Operations.OperationRecordDto;

namespace Sideport.Api.Tests;

public partial class ApiSmokeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecoveryRenewal_RunningOrCheckpointedRedeliveryNeverInstalls(bool checkpointed)
    {
        using RecoveryHarness test = await RecoveryHarness.CreateAsync();
        RecoveryRecord receipt = await test.ReconcileAsync();
        RecoveryRecord child = await test.SubmitAsync(receipt);
        await test.Store.UpdateAsync(child with
        {
            Status = "running",
            StartedAt = DateTimeOffset.UtcNow,
            RecoveryCheckpoint = checkpointed
                ? new Operations.OperationRecoveryCheckpointDto(
                    test.FutureExpiry, test.Source.Target.CatalogSha256!, DateTimeOffset.UtcNow)
                : null,
        });
        await test.ProcessAsync(child.OperationId);
        await test.ProcessAsync(child.OperationId);
        Assert.Equal(0, test.Controller.InstallCalls);
        Assert.Empty(test.Identity.AllowCertificateCreation);
        Assert.NotEqual("succeeded", (await test.Store.FindAsync(child.OperationId))!.Status);
        await test.AssertQuarantinedAsync();
    }

    [Fact]
    public async Task RecoveryRenewal_RejectedCheckpointTransitionCannotAuthorizeInstall()
    {
        using RecoveryHarness test = await RecoveryHarness.CreateAsync();
        RecoveryRecord receipt = await test.ReconcileAsync();
        RecoveryRecord child = await test.SubmitAsync(receipt);
        test.Identity.OnPrepare = async () =>
        {
            RecoveryRecord current = (await test.Store.FindAsync(child.OperationId))!;
            await test.Store.UpdateAsync(current with
            {
                RecoveryCheckpoint = new Operations.OperationRecoveryCheckpointDto(
                    test.FutureExpiry, test.Source.Target.CatalogSha256!, DateTimeOffset.UtcNow),
            });
        };
        await test.ProcessAsync(child.OperationId);
        Assert.Equal(0, test.Controller.InstallCalls);
        Assert.Equal("unknown", (await test.Store.FindAsync(child.OperationId))!.Status);
        await test.AssertQuarantinedAsync();
    }

    [Theory]
    [InlineData("observe", null)]
    [InlineData("observe", "missing")]
    [InlineData("observe", "failed")]
    [InlineData("execute", null)]
    [InlineData("execute", "missing")]
    [InlineData("execute", "failed")]
    [InlineData("checkpoint", null)]
    [InlineData("checkpoint", "missing")]
    [InlineData("checkpoint", "failed")]
    public async Task RecoveryRenewal_RequiresRealPriorVerificationAtEveryBoundary(string boundary, string? invalid)
    {
        using RecoveryHarness test = await RecoveryHarness.CreateAsync();
        async Task InvalidateAsync()
        {
            if (invalid == "failed")
            {
                RecoveryRecord prior = (await test.Store.FindAsync(test.PriorOperationId))!;
                await test.Store.UpdateAsync(prior with { Status = "failed" });
            }
            else
            {
                await test.Registry.UpsertAsync((await test.RegistrationAsync()) with
                {
                    LastVerifiedOperationId = invalid is null ? null : "op_missing_verification",
                });
            }
        }

        RecoveryRecord result;
        if (boundary == "observe")
        {
            await InvalidateAsync();
            result = await test.ReconcileAsync();
            Assert.NotEqual(true, result.Result?.RenewalEligible);
        }
        else
        {
            RecoveryRecord receipt = await test.ReconcileAsync();
            RecoveryRecord child = await test.SubmitAsync(receipt);
            if (boundary == "checkpoint")
                test.Identity.OnPrepare = InvalidateAsync;
            else
                await InvalidateAsync();
            await test.ProcessAsync(child.OperationId);
            result = (await test.Store.FindAsync(child.OperationId))!;
        }
        Assert.NotEqual("succeeded", result.Status);
        Assert.NotEqual(true, result.Result?.Success);
        Assert.Equal(0, test.Controller.InstallCalls);
        Assert.Equal("held", (await test.SchedulerAsync()).Concurrency.LockState);
        await test.AssertSourceUnchangedAsync();
    }

    [Fact]
    public async Task RecoveryRenewal_CorruptHistoryPreventsStartupBeforeMutations()
    {
        string directory = Path.Combine(Path.GetTempPath(), "sideport-startup-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "operations.json");
            await File.WriteAllTextAsync(path, "{not-json");
            using var factory = Factory(apiToken: "s3cr3t-token", stateDirectory: directory);
            Assert.Throws<Operations.OperationStoreException>(() => factory.CreateClient());
            Assert.Equal("{not-json", await File.ReadAllTextAsync(path));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-60)]
    [InlineData(0)]
    [InlineData(60)]
    public async Task RecoveryRenewal_TrustedWifiObservationDoesNotUnlock_ExplicitChildVerifies(
        int? observedExpiryOffsetSeconds)
    {
        using RecoveryHarness test = await RecoveryHarness.CreateAsync(observedExpiryOffsetSeconds);
        RecoveryRecord receipt = await test.ReconcileAsync();

        Assert.Equal("succeeded", receipt.Status);
        Assert.True(receipt.Result!.RenewalEligible);
        Assert.False(receipt.Result.Success);
        Assert.False(receipt.Result.SafeToRerun);
        Assert.Equal(test.Controller.ProfileExpiry, receipt.Result.ExpiresAt);
        Assert.Equal(test.Source.OperationId, receipt.ParentOperationId);
        Assert.Equal(test.Source.OperationId, receipt.Result.ReconciledOperationId);
        Assert.Equal(0, test.Controller.InstallCalls);
        await test.AssertQuarantinedAsync();

        using HttpResponseMessage ordinaryRerun = await test.Client.PostAsJsonAsync(
            $"/api/operations/{test.Source.OperationId}/rerun",
            new Operations.OperationActionRequest("ordinary-rerun"));
        Assert.Equal(HttpStatusCode.Conflict, ordinaryRerun.StatusCode);
        Assert.Empty((await test.Store.ListAsync(limit: null)).Where(operation =>
            operation.RecoveryIntent is not null));

        RecoveryRecord child = await test.SubmitAsync(receipt);
        Assert.Equal("refresh", child.Type);
        Assert.Equal("queued", child.Status);
        Assert.Equal(test.Source.OperationId, child.ParentOperationId);
        Assert.Equal("superseding-renewal", child.RecoveryIntent!.Kind);
        Assert.Equal(receipt.OperationId, child.RecoveryIntent.ReceiptOperationId);
        Assert.Equal(test.Source.Target, child.Target);
        Assert.Null(child.RecoveryCheckpoint);

        bool durableCheckpointSeen = false;
        test.Controller.OnInstall = async () =>
        {
            // Reopen the file: an in-memory checkpoint is not sufficient evidence.
            var reopened = new Operations.OperationStore(Path.Combine(test.StateDirectory, "operations.json"));
            RecoveryRecord persisted = (await reopened.FindAsync(child.OperationId))!;
            Assert.Equal("running", persisted.Status);
            Assert.NotNull(persisted.RecoveryCheckpoint);
            Assert.Equal(test.FutureExpiry, persisted.RecoveryCheckpoint.PreparedExpiresAt);
            Assert.Equal(test.Source.Target.CatalogSha256, persisted.RecoveryCheckpoint.PinnedArtifactSha256);
            Assert.True(persisted.RecoveryCheckpoint.MutationStartedAt <= DateTimeOffset.UtcNow);
            Assert.NotEqual(true, persisted.Result?.Success);
            Assert.Equal(test.PriorOperationId, (await test.RegistrationAsync()).LastVerifiedOperationId);
            durableCheckpointSeen = true;
        };

        await test.ProcessAsync(child.OperationId);
        RecoveryRecord completed = (await test.Store.FindAsync(child.OperationId))!;
        Assert.True(durableCheckpointSeen);
        Assert.Equal("succeeded", completed.Status);
        Assert.True(completed.Result!.Success);
        Assert.Equal("1.0", completed.Result.Version);
        Assert.Equal(test.Source.Target.BundleId, completed.Result.BundleId);
        Assert.Equal(test.FutureExpiry, completed.Result.ExpiresAt);
        Assert.Contains(completed.Stages, stage =>
            stage.Id == "verify" && stage.Status == "succeeded" && stage.CompletedAt is not null);
        Assert.Equal(1, test.Controller.InstallCalls);
        Assert.NotEqual(DeviceConnection.Usb, test.Controller.RequiredConnection);
        Assert.Equal(new[] { false }, test.Identity.AllowCertificateCreation);
        Assert.Equal(0, test.Identity.LegacyPrepareCalls);
        Assert.False(Directory.Exists(Path.Combine(
            test.RootDirectory,
            "signed",
            "TEST-UDID",
            "recovery",
            completed.RecoveryCheckpoint!.ArtifactSnapshotId!)));
        Assert.True(test.Controller.FreshReads >= 3);
        Assert.Equal(0, test.Controller.CachedReads);
        Assert.Equal(0, test.Controller.PairCalls);
        Assert.Equal(child.OperationId, (await test.RegistrationAsync()).LastVerifiedOperationId);
        Assert.Equal("active", (await test.RegistrationAsync()).Lifecycle);
        await test.AssertSourceUnchangedAsync();
        Assert.True(Operations.OperationReconciliationEvidence.IsResolved(
            test.Source, await test.Store.ListAsync(limit: null)));
        Operations.SchedulerStatusDto scheduler = await test.SchedulerAsync();
        Assert.False(scheduler.Enabled);
        Assert.Equal("idle", scheduler.Concurrency.LockState);
        await test.ProcessAsync(child.OperationId);
        Assert.Equal(completed.CompletedAt, (await test.Store.FindAsync(child.OperationId))!.CompletedAt);
        Assert.Equal(1, test.Controller.InstallCalls);
    }

    [Theory]
    [InlineData("different-expired-profile")]
    [InlineData("different-future-profile")]
    [InlineData("different-version")]
    [InlineData("expected-expiry-not-elapsed")]
    public async Task RecoveryRenewal_DifferingKnownEvidenceNeverBecomesEligible(string mismatch)
    {
        using RecoveryHarness test = await RecoveryHarness.CreateAsync();
        switch (mismatch)
        {
            case "different-expired-profile":
                test.Controller.ProfileExpiry = test.ExpectedExpiry.AddSeconds(-61);
                break;
            case "different-future-profile":
                test.Controller.ProfileExpiry = test.FutureExpiry;
                break;
            case "different-version":
                test.Controller.Version = "2.0";
                break;
            case "expected-expiry-not-elapsed":
                await test.Store.UpdateAsync(test.Source with
                {
                    Result = test.Source.Result! with { ExpiresAt = test.FutureExpiry },
                });
                break;
        }

        RecoveryRecord receipt = await test.ReconcileAsync();
        Assert.Equal("blocked", receipt.Status);
        Assert.NotEqual(true, receipt.Result?.RenewalEligible);
        Assert.NotEqual(true, receipt.Result?.Success);
        Assert.NotEqual(true, receipt.Result?.SafeToRerun);
        Assert.Equal("reconciliation-evidence-mismatch", receipt.Error?.Code);
        Assert.Equal(0, test.Controller.InstallCalls);
        Assert.Equal(test.PriorOperationId, (await test.RegistrationAsync()).LastVerifiedOperationId);
        Assert.Equal("held", (await test.SchedulerAsync()).Concurrency.LockState);
    }

    [Theory]
    [InlineData("device")]
    [InlineData("bundle")]
    [InlineData("version")]
    [InlineData("team")]
    [InlineData("account")]
    [InlineData("hash")]
    [InlineData("expiry")]
    [InlineData("missing-device")]
    [InlineData("missing-bundle")]
    [InlineData("missing-version")]
    [InlineData("missing-team")]
    [InlineData("missing-account")]
    [InlineData("missing-hash")]
    [InlineData("missing-expiry")]
    [InlineData("confirm")]
    [InlineData("receipt")]
    public async Task RecoveryRenewal_RequiresConfirmationReceiptAndEveryExactLineageEcho(string mutation)
    {
        using RecoveryHarness test = await RecoveryHarness.CreateAsync();
        RecoveryRecord receipt = await test.ReconcileAsync();
        Operations.OperationActionRequest request = test.Request(receipt);
        Operations.SupersedingRenewalRequest intent = request.SupersedingRenewal!;
        intent = mutation switch
        {
            "device" => intent with { DeviceUdid = "OTHER-TEST-UDID" },
            "bundle" => intent with { BundleId = "com.example.other" },
            "version" => intent with { Version = "2.0" },
            "team" => intent with { TeamId = "OTHER-TEAM" },
            "account" => intent with { AccountProfileId = "other-account" },
            "hash" => intent with { CatalogSha256 = new string('0', 64) },
            "expiry" => intent with { ExpectedExpiresAt = test.ExpectedExpiry.AddSeconds(2) },
            "missing-device" => intent with { DeviceUdid = null },
            "missing-bundle" => intent with { BundleId = null },
            "missing-version" => intent with { Version = null },
            "missing-team" => intent with { TeamId = null },
            "missing-account" => intent with { AccountProfileId = null },
            "missing-hash" => intent with { CatalogSha256 = null },
            "missing-expiry" => intent with { ExpectedExpiresAt = null },
            "confirm" => intent with { Confirm = false },
            "receipt" => intent with { ReceiptOperationId = "op_missing_receipt" },
            _ => throw new ArgumentOutOfRangeException(nameof(mutation)),
        };
        using HttpResponseMessage response = await test.PostRenewalAsync(request with { SupersedingRenewal = intent });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Empty((await test.Store.ListAsync(limit: null)).Where(operation => operation.RecoveryIntent is not null));
        Assert.Empty(test.Identity.AllowCertificateCreation);
        Assert.Equal(0, test.Controller.InstallCalls);
        await test.AssertQuarantinedAsync();
    }

    [Fact]
    public async Task RecoveryRenewal_RequiresIdempotencyAndAuthenticatedAuthority()
    {
        using RecoveryHarness test = await RecoveryHarness.CreateAsync();
        RecoveryRecord receipt = await test.ReconcileAsync();
        using HttpResponseMessage missingKey = await test.PostRenewalAsync(test.Request(receipt) with { IdempotencyKey = null });
        Assert.Equal(HttpStatusCode.BadRequest, missingKey.StatusCode);
        test.Client.DefaultRequestHeaders.Authorization = null;
        using HttpResponseMessage anonymous = await test.PostRenewalAsync(test.Request(receipt));
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Empty((await test.Store.ListAsync(limit: null)).Where(operation => operation.RecoveryIntent is not null));
        Assert.Equal(0, test.Controller.InstallCalls);
    }

    [Fact]
    public async Task RecoveryRenewal_ReceiptCannotAuthorizeADifferentUnknownSource()
    {
        using RecoveryHarness test = await RecoveryHarness.CreateAsync();
        RecoveryRecord receipt = await test.ReconcileAsync();
        RecoveryRecord other = test.Source with
        {
            OperationId = $"op_other_source_{Guid.NewGuid():N}",
            IdempotencyKey = $"other-source-{Guid.NewGuid():N}",
        };
        await test.Store.AddIfIdempotentMissingAsync(other);
        using HttpResponseMessage rejected = await test.Client.PostAsJsonAsync(
            $"/api/operations/{other.OperationId}/rerun", test.Request(receipt));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rejected.StatusCode);
        Assert.Equal("recovery-receipt-invalid",
            (await rejected.Content.ReadFromJsonAsync<Operations.OperationErrorDto>())!.Error);
        Assert.Empty((await test.Store.ListAsync(limit: null)).Where(operation => operation.RecoveryIntent is not null));
        Assert.Equal(0, test.Controller.InstallCalls);
        await test.AssertQuarantinedAsync();
    }

    [Fact]
    public async Task RecoveryRenewal_SameKeyReplaysAfterCompletionAndReceiptAging_ChangedBodyConflicts()
    {
        using RecoveryHarness test = await RecoveryHarness.CreateAsync();
        RecoveryRecord receipt = await test.ReconcileAsync();
        Operations.OperationActionRequest request = test.Request(receipt);
        using HttpResponseMessage accepted = await test.PostRenewalAsync(request);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        RecoveryRecord child = (await accepted.Content.ReadFromJsonAsync<RecoveryRecord>())!;
        using HttpResponseMessage queuedReplay = await test.PostRenewalAsync(request);
        Assert.Equal(HttpStatusCode.OK, queuedReplay.StatusCode);
        Assert.Equal(child.OperationId, (await queuedReplay.Content.ReadFromJsonAsync<RecoveryRecord>())!.OperationId);

        await test.ProcessAsync(child.OperationId);
        Assert.Equal("succeeded", (await test.Store.FindAsync(child.OperationId))!.Status);
        await test.AgeReceiptAsync(receipt);
        using HttpResponseMessage completedReplay = await test.PostRenewalAsync(request);
        Assert.Equal(HttpStatusCode.OK, completedReplay.StatusCode);
        Assert.Equal(child.OperationId, (await completedReplay.Content.ReadFromJsonAsync<RecoveryRecord>())!.OperationId);
        using HttpResponseMessage changed = await test.PostRenewalAsync(request with
        {
            SupersedingRenewal = request.SupersedingRenewal! with { Version = "2.0" },
        });
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        Assert.Equal("idempotency-target-conflict",
            (await changed.Content.ReadFromJsonAsync<Operations.OperationErrorDto>())!.Error);
        using HttpResponseMessage differentKey = await test.PostRenewalAsync(request with { IdempotencyKey = "another-key" });
        Assert.Equal(HttpStatusCode.Conflict, differentKey.StatusCode);
        Assert.Single((await test.Store.ListAsync(limit: null)).Where(operation => operation.RecoveryIntent is not null));
        Assert.Equal(1, test.Controller.InstallCalls);
    }

    [Fact]
    public async Task RecoveryRenewal_ConcurrentSameKeyCreatesOneChild_DifferentKeyCannotConsumeReceipt()
    {
        using RecoveryHarness test = await RecoveryHarness.CreateAsync();
        RecoveryRecord receipt = await test.ReconcileAsync();
        Operations.OperationActionRequest request = test.Request(receipt);
        HttpResponseMessage[] responses = await Task.WhenAll(
            test.PostRenewalAsync(request), test.PostRenewalAsync(request));
        try
        {
            Assert.Single(responses.Where(response => response.StatusCode == HttpStatusCode.Accepted));
            Assert.Single(responses.Where(response => response.StatusCode == HttpStatusCode.OK));
            RecoveryRecord first = (await responses[0].Content.ReadFromJsonAsync<RecoveryRecord>())!;
            RecoveryRecord second = (await responses[1].Content.ReadFromJsonAsync<RecoveryRecord>())!;
            Assert.Equal(first.OperationId, second.OperationId);
            using HttpResponseMessage reused = await test.PostRenewalAsync(request with { IdempotencyKey = "different-key" });
            Assert.Equal(HttpStatusCode.Conflict, reused.StatusCode);
            Assert.Equal("recovery-receipt-consumed",
                (await reused.Content.ReadFromJsonAsync<Operations.OperationErrorDto>())!.Error);
            Assert.Single((await test.Store.ListAsync(limit: null)).Where(operation => operation.RecoveryIntent is not null));
            Assert.Equal(0, test.Controller.InstallCalls);
        }
        finally
        {
            foreach (HttpResponseMessage response in responses)
                response.Dispose();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecoveryRenewal_ExpiredReceiptBlocksSubmissionAndQueuedExecution(bool alreadyQueued)
    {
        using RecoveryHarness test = await RecoveryHarness.CreateAsync();
        RecoveryRecord receipt = await test.ReconcileAsync();
        RecoveryRecord? child = alreadyQueued ? await test.SubmitAsync(receipt) : null;
        await test.AgeReceiptAsync(receipt);
        if (child is null)
        {
            using HttpResponseMessage rejected = await test.PostRenewalAsync(test.Request(receipt));
            Assert.Equal(HttpStatusCode.UnprocessableEntity, rejected.StatusCode);
            Assert.Equal("recovery-receipt-invalid",
                (await rejected.Content.ReadFromJsonAsync<Operations.OperationErrorDto>())!.Error);
        }
        else
        {
            await test.ProcessAsync(child.OperationId);
            Assert.Equal("blocked", (await test.Store.FindAsync(child.OperationId))!.Status);
        }
        Assert.Equal(0, test.Controller.InstallCalls);
        Assert.Empty(test.Identity.AllowCertificateCreation);
        await test.AssertQuarantinedAsync();
    }

    [Theory]
    [InlineData("artifact")]
    [InlineData("source")]
    [InlineData("receipt")]
    [InlineData("registration")]
    [InlineData("installed-version")]
    [InlineData("installed-expiry")]
    [InlineData("missing-app")]
    [InlineData("trust")]
    [InlineData("identity")]
    [InlineData("actor")]
    [InlineData("ownership")]
    [InlineData("other-unknown")]
    [InlineData("other-running")]
    public async Task RecoveryRenewal_QueuedDriftOrConflictingWorkBlocksBeforeInstall(string mutation)
    {
        using RecoveryHarness test = await RecoveryHarness.CreateAsync();
        RecoveryRecord receipt = await test.ReconcileAsync();
        RecoveryRecord child = await test.SubmitAsync(receipt);
        switch (mutation)
        {
            case "artifact":
                WriteTestIpa(test.RootDirectory, test.BundleId, "Changed bytes", "1", "1.0");
                break;
            case "source":
                await test.Store.UpdateAsync(test.Source with
                {
                    Target = test.Source.Target with { CatalogSha256 = new string('0', 64) },
                });
                break;
            case "receipt":
                await test.Store.UpdateAsync(receipt with { ParentOperationId = "op_unrelated_source" });
                break;
            case "registration":
                await test.Registry.UpsertAsync((await test.RegistrationAsync()) with { TeamId = "OTHER-TEAM" });
                break;
            case "installed-version":
                test.Controller.Version = "2.0";
                break;
            case "installed-expiry":
                test.Controller.ProfileExpiry = test.ExpectedExpiry.AddHours(-1);
                break;
            case "missing-app":
                test.Controller.Installed = false;
                break;
            case "trust":
                test.Controller.Trusted = false;
                break;
            case "identity":
                test.Identity.State = "missing";
                break;
            case "actor":
                await test.Store.UpdateAsync(child with
                {
                    Actor = new Operations.OperationActorDto("oidc", "Owner", "forged-owner"),
                    ActorMemberId = "member-not-current",
                });
                break;
            case "ownership":
                KnownDeviceStore known = test.Factory.Services.GetRequiredService<KnownDeviceStore>();
                KnownDeviceRecord device = (await known.FindAsync("TEST-UDID"))!;
                await known.UpsertAsync(device with { OwnerMemberId = "different-owner" });
                break;
            case "other-unknown":
            case "other-running":
                await test.Store.AddIfIdempotentMissingAsync(test.Source with
                {
                    OperationId = $"op_conflict_{Guid.NewGuid():N}",
                    IdempotencyKey = $"conflict-{Guid.NewGuid():N}",
                    Status = mutation == "other-running" ? "running" : "unknown",
                    Target = test.Source.Target with { BundleId = "com.example.other" },
                });
                break;
        }

        await test.ProcessAsync(child.OperationId);
        RecoveryRecord terminal = (await test.Store.FindAsync(child.OperationId))!;
        Assert.Equal("blocked", terminal.Status);
        Assert.NotEqual(true, terminal.Result?.Success);
        Assert.Null(terminal.RecoveryCheckpoint);
        Assert.Equal(0, test.Controller.InstallCalls);
        await test.AssertQuarantinedAsync();
    }

    [Theory]
    [InlineData("actor")]
    [InlineData("registration")]
    [InlineData("source")]
    public async Task RecoveryRenewal_RechecksAuthorityAndLineageAtMutationCheckpoint(string mutation)
    {
        using RecoveryHarness test = await RecoveryHarness.CreateAsync();
        RecoveryRecord receipt = await test.ReconcileAsync();
        RecoveryRecord child = await test.SubmitAsync(receipt);
        test.Identity.OnPrepare = async () =>
        {
            if (mutation == "actor")
            {
                RecoveryRecord running = (await test.Store.FindAsync(child.OperationId))!;
                await test.Store.UpdateAsync(running with { ActorMemberId = "revoked-member" });
            }
            else if (mutation == "registration")
                await test.Registry.UpsertAsync((await test.RegistrationAsync()) with { TeamId = "OTHER-TEAM" });
            else
                await test.Store.UpdateAsync(test.Source with
                {
                    Target = test.Source.Target with { CatalogSha256 = new string('0', 64) },
                });
        };

        await test.ProcessAsync(child.OperationId);
        RecoveryRecord terminal = (await test.Store.FindAsync(child.OperationId))!;
        Assert.NotEqual("succeeded", terminal.Status);
        Assert.NotEqual(true, terminal.Result?.Success);
        Assert.Null(terminal.RecoveryCheckpoint);
        Assert.Equal(0, test.Controller.InstallCalls);
        Assert.Equal(new[] { false }, test.Identity.AllowCertificateCreation);
        await test.AssertQuarantinedAsync();
    }

    [Theory]
    [InlineData("install-failure")]
    [InlineData("missing-profile")]
    [InlineData("wrong-expiry")]
    [InlineData("wrong-version")]
    [InlineData("missing-app")]
    [InlineData("unreadable")]
    public async Task RecoveryRenewal_UnverifiableMutationStaysUnknownAndNeverAutomaticallyReinstalls(string outcome)
    {
        using RecoveryHarness test = await RecoveryHarness.CreateAsync();
        RecoveryRecord receipt = await test.ReconcileAsync();
        RecoveryRecord child = await test.SubmitAsync(receipt);
        test.Controller.Readback = outcome;
        await test.ProcessAsync(child.OperationId);
        RecoveryRecord terminal = (await test.Store.FindAsync(child.OperationId))!;
        Assert.Equal("unknown", terminal.Status);
        Assert.False(terminal.Result!.Success);
        Assert.NotNull(terminal.RecoveryCheckpoint);
        Assert.Equal(test.FutureExpiry, terminal.RecoveryCheckpoint.PreparedExpiresAt);
        Assert.False(terminal.Retryable);
        Assert.False(terminal.Rerunnable);
        Assert.Equal(1, test.Controller.InstallCalls);
        await test.AssertSourceUnchangedAsync();
        await test.AssertQuarantinedAsync();

        await test.ProcessAsync(child.OperationId);
        using HttpResponseMessage sameReceipt = await test.PostRenewalAsync(
            test.Request(receipt) with { IdempotencyKey = "retry-with-different-key" });
        Assert.Equal(HttpStatusCode.Conflict, sameReceipt.StatusCode);
        Assert.Equal(1, test.Controller.InstallCalls);
        Assert.Single((await test.Store.ListAsync(limit: null)).Where(operation => operation.RecoveryIntent is not null));
    }

    [Fact]
    public async Task RecoveryRenewal_ActiveManagedTransferCannotBeBypassedByAnEligibleReceipt()
    {
        using RecoveryHarness test = await RecoveryHarness.CreateAsync();
        RecoveryRecord receipt = await test.ReconcileAsync();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        test.Controller.OnInstall = async () =>
        {
            started.TrySetResult();
            await release.Task;
        };
        RefreshOrchestrator orchestrator = test.Factory.Services.GetRequiredService<RefreshOrchestrator>();
        Task<RefreshResult> active = orchestrator.RefreshAsync("TEST-UDID", test.BundleId);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(orchestrator.IsDeviceMutationActive("TEST-UDID"));
            using HttpResponseMessage rejected = await test.PostRenewalAsync(test.Request(receipt));
            Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
            Assert.Equal("device-operation-still-active",
                (await rejected.Content.ReadFromJsonAsync<Operations.OperationErrorDto>())!.Error);
            Assert.Empty((await test.Store.ListAsync(limit: null)).Where(operation => operation.RecoveryIntent is not null));
            Assert.Equal(1, test.Controller.InstallCalls);
        }
        finally
        {
            release.TrySetResult();
            await active.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task RecoveryRenewal_CheckpointPersistenceFailurePreventsDeviceMutation()
    {
        using RecoveryHarness test = await RecoveryHarness.CreateAsync();
        bool checkpointAttempted = false;
        var policy = RefreshExecutionPolicy.RecoveryRenewal with
        {
            Recovery = new RefreshRecoveryPlan(
                test.Source.Target.CatalogSha256!,
                (checkpoint, _) =>
                {
                    checkpointAttempted = true;
                    Assert.Equal(test.FutureExpiry, checkpoint.PreparedExpiresAt);
                    Assert.Equal(test.Source.Target.CatalogSha256, checkpoint.PinnedArtifactSha256);
                    Assert.Equal("TEST-UDID", checkpoint.DeviceUdid);
                    Assert.Equal(test.BundleId, checkpoint.BundleId);
                    return Task.FromException(new IOException("Simulated durable checkpoint write failure."));
                }),
        };
        RefreshResult result = await test.Factory.Services.GetRequiredService<RefreshOrchestrator>()
            .RefreshAsync("TEST-UDID", test.BundleId, policy);
        Assert.True(checkpointAttempted);
        Assert.False(result.Success);
        Assert.Equal(0, test.Controller.InstallCalls);
        Assert.Equal(new[] { false }, test.Identity.AllowCertificateCreation);
        await test.AssertQuarantinedAsync();
    }

    [Fact]
    public async Task RecoveryRenewal_StartupBarrierRecoversRecentRunningBeforeHttpAndDoesNotReplay()
    {
        using RecoveryHarness test = await RecoveryHarness.CreateAsync();
        await test.Store.UpdateAsync(test.Source with
        {
            Status = "running",
            StartedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Stages = test.Source.Stages.Select(stage => stage.Id == "refresh"
                ? stage with { Status = "running" }
                : stage).ToArray(),
        });
        test.Restart(operationWorker: true);
        using HttpResponseMessage health = await test.Client.GetAsync("/healthz");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        RecoveryRecord recovered = (await test.Store.FindAsync(test.Source.OperationId))!;
        Assert.Equal("unknown", recovered.Status);
        Assert.Equal("operation-terminal-state-unknown", recovered.Error?.Code);
        Assert.Equal(test.ExpectedExpiry, recovered.Result!.ExpiresAt);
        Assert.False(recovered.Retryable);
        await test.ProcessAsync(recovered.OperationId);
        Assert.Equal(0, test.Controller.InstallCalls);
        Assert.Empty(test.Identity.AllowCertificateCreation);
    }

    [Theory]
    [InlineData(false, "failed")]
    [InlineData(true, "unknown")]
    public async Task RecoveryRenewal_RestartAtMutationBoundaryNeverReplays(
        bool mutationCheckpointPersisted, string expectedStatus)
    {
        using RecoveryHarness test = await RecoveryHarness.CreateAsync();
        RecoveryRecord receipt = await test.ReconcileAsync();
        RecoveryRecord child = await test.SubmitAsync(receipt);
        await test.Store.UpdateAsync(child with
        {
            Status = "running",
            StartedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            RecoveryCheckpoint = mutationCheckpointPersisted
                ? new Operations.OperationRecoveryCheckpointDto(
                    test.FutureExpiry, test.Source.Target.CatalogSha256!, DateTimeOffset.UtcNow)
                : null,
        });
        test.Restart(operationWorker: true);
        RecoveryRecord recovered = (await test.Store.FindAsync(child.OperationId))!;
        Assert.Equal(expectedStatus, recovered.Status);
        Assert.Equal(child.RecoveryIntent, recovered.RecoveryIntent);
        Assert.Equal(mutationCheckpointPersisted, recovered.RecoveryCheckpoint is not null);
        await test.ProcessAsync(child.OperationId);
        Assert.Equal(0, test.Controller.InstallCalls);
        Assert.Empty(test.Identity.AllowCertificateCreation);
        await test.AssertQuarantinedAsync();
    }

    [Theory]
    [InlineData("running", false)]
    [InlineData("recovery-required", false)]
    [InlineData("recovery-required", true)]
    public async Task RecoveryRenewal_RestartFinalizesDurableVerificationWithoutDeviceReadOrInstall(
        string initialStatus, bool explicitRetry)
    {
        using RecoveryHarness test = await RecoveryHarness.CreateAsync();
        RecoveryRecord receipt = await test.ReconcileAsync();
        RecoveryRecord child = await test.SubmitAsync(receipt);
        DateTimeOffset verifiedAt = DateTimeOffset.UtcNow;
        await test.Store.UpdateAsync(child with
        {
            Status = initialStatus,
            StartedAt = verifiedAt.AddSeconds(-1),
            UpdatedAt = verifiedAt,
            RecoveryCheckpoint = new Operations.OperationRecoveryCheckpointDto(
                test.FutureExpiry, test.Source.Target.CatalogSha256!, verifiedAt.AddSeconds(-1)),
            Result = new Operations.OperationResultDto(
                true, test.BundleId, test.FutureExpiry, null, Version: "1.0"),
            Stages = child.Stages.Select(stage => stage.Id is "renew" or "verify"
                ? stage with { Status = "succeeded", StartedAt = verifiedAt.AddSeconds(-1), CompletedAt = verifiedAt }
                : stage).ToArray(),
        });
        int reads = test.Controller.FreshReads;
        test.Controller.RejectAllReads = true;
        test.Restart(operationWorker: !explicitRetry);
        if (explicitRetry)
        {
            using HttpResponseMessage retry = await test.Client.PostAsJsonAsync(
                $"/api/operations/{child.OperationId}/retry",
                new Operations.OperationActionRequest("finish-verified-recovery"));
            Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
            await test.ProcessAsync(child.OperationId);
        }
        else
        {
            await WaitForTerminalOperationAsync(test.Client, child.OperationId);
        }
        RecoveryRecord completed = (await test.Store.FindAsync(child.OperationId))!;
        Assert.Equal("succeeded", completed.Status);
        Assert.True(completed.Result!.Success);
        Assert.Equal(child.OperationId, (await test.RegistrationAsync()).LastVerifiedOperationId);
        Assert.Equal(reads, test.Controller.FreshReads);
        Assert.Equal(0, test.Controller.InstallCalls);
        Assert.Empty(test.Identity.AllowCertificateCreation);
        await test.AssertSourceUnchangedAsync();
        Assert.True(Operations.OperationReconciliationEvidence.IsResolved(
            test.Source, await test.Store.ListAsync(limit: null)));
    }

    private sealed class RecoveryHarness : IDisposable
    {
        private RecoveryHarness(string root, DateTimeOffset expectedExpiry, DateTimeOffset futureExpiry, int? offset)
        {
            RootDirectory = root;
            StateDirectory = Path.Combine(root, "state");
            ExpectedExpiry = expectedExpiry;
            FutureExpiry = futureExpiry;
            IpaPath = WriteTestIpa(root, BundleId, "Recovery test", "1", "1.0");
            Identity = new RecoverySigningIdentity(root, futureExpiry);
            Controller = new RecoveryDeviceController(BundleId, futureExpiry)
            {
                ProfileExpiry = offset is { } seconds ? expectedExpiry.AddSeconds(seconds) : null,
            };
            Factory = CreateFactory(operationWorker: false);
            Client = HttpsTokenClient(Factory);
        }

        public string RootDirectory { get; }
        public string StateDirectory { get; }
        public string IpaPath { get; }
        public string BundleId => "com.example.recovery";
        public DateTimeOffset ExpectedExpiry { get; }
        public DateTimeOffset FutureExpiry { get; }
        public RecoveryRecord Source { get; private set; } = null!;
        public string PriorOperationId => $"{Source.OperationId}_prior";
        public RecoverySigningIdentity Identity { get; }
        public RecoveryDeviceController Controller { get; }
        public WebApplicationFactory<Program> Factory { get; private set; }
        public HttpClient Client { get; private set; }
        public Operations.OperationStore Store => Factory.Services.GetRequiredService<Operations.OperationStore>();
        public IAppRegistry Registry => Factory.Services.GetRequiredService<IAppRegistry>();

        public static async Task<RecoveryHarness> CreateAsync(int? observedExpiryOffsetSeconds = null)
        {
            string root = Path.Combine(Directory.GetCurrentDirectory(), ".recovery-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var test = new RecoveryHarness(
                root, DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(7), observedExpiryOffsetSeconds);
            try
            {
                await PrepareAppleTeamAsync(test.Client);
                test.Source = await SeedUnknownRefreshAsync(
                    test.Factory, test.BundleId, test.IpaPath, test.ExpectedExpiry, $"op_recovery_{Guid.NewGuid():N}");
                return test;
            }
            catch
            {
                test.Dispose();
                throw;
            }
        }

        private WebApplicationFactory<Program> CreateFactory(bool operationWorker) =>
            ApiSmokeTests.Factory(
                apiToken: "s3cr3t-token",
                stateDirectory: StateDirectory,
                personalAppleId: "developer@example.com",
                personalApplePassword: "fake-recovery-password",
                personalApplePortal: new StubApplePortal(),
                operationWorker: operationWorker,
                deviceController: Controller,
                signingIdentityProvider: Identity)
            .WithWebHostBuilder(builder =>
                builder.UseSetting("Sideport:Orchestrator:WorkDirectory", Path.Combine(RootDirectory, "signed")));

        public void Restart(bool operationWorker)
        {
            Client.Dispose();
            Factory.Dispose();
            Factory = CreateFactory(operationWorker);
            Client = HttpsTokenClient(Factory);
        }

        public Task ProcessAsync(string operationId) =>
            Factory.Services.GetRequiredService<Operations.OperationService>().ProcessQueuedOperationAsync(operationId);

        public async Task<RecoveryRecord> ReconcileAsync()
        {
            using HttpResponseMessage response = await Client.PostAsJsonAsync(
                $"/api/operations/{Source.OperationId}/reconcile",
                new Operations.OperationReconcileRequest($"observe-{Guid.NewGuid():N}"));
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            RecoveryRecord queued = (await response.Content.ReadFromJsonAsync<RecoveryRecord>())!;
            await ProcessAsync(queued.OperationId);
            return (await Store.FindAsync(queued.OperationId))!;
        }

        public Operations.OperationActionRequest Request(RecoveryRecord receipt) => new(
            IdempotencyKey: $"renew-{receipt.OperationId}",
            SupersedingRenewal: new Operations.SupersedingRenewalRequest(
                receipt.OperationId,
                Confirm: true,
                Source.Target.DeviceUdid,
                Source.Target.BundleId,
                Source.Target.Version,
                Source.Target.TeamId,
                Source.Target.AccountProfileId,
                Source.Target.CatalogSha256,
                ExpectedExpiry));

        public Task<HttpResponseMessage> PostRenewalAsync(Operations.OperationActionRequest request) =>
            Client.PostAsJsonAsync($"/api/operations/{Source.OperationId}/rerun", request);

        public async Task<RecoveryRecord> SubmitAsync(RecoveryRecord receipt)
        {
            using HttpResponseMessage response = await PostRenewalAsync(Request(receipt));
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<RecoveryRecord>())!;
        }

        public Task<RecoveryRecord> AgeReceiptAsync(RecoveryRecord receipt)
        {
            DateTimeOffset old = DateTimeOffset.UtcNow.AddMinutes(-6);
            return Store.UpdateAsync(receipt with
            {
                CreatedAt = old.AddSeconds(-1),
                StartedAt = old.AddSeconds(-1),
                UpdatedAt = old,
                CompletedAt = old,
            });
        }

        public async Task<AppRegistration> RegistrationAsync() =>
            (await Registry.FindAsync("TEST-UDID", BundleId))!;

        public async Task<Operations.SchedulerStatusDto> SchedulerAsync() =>
            (await Client.GetFromJsonAsync<Operations.SchedulerStatusDto>("/api/scheduler/status"))!;

        public async Task AssertSourceUnchangedAsync()
        {
            RecoveryRecord source = (await Store.FindAsync(Source.OperationId))!;
            Assert.Equal("unknown", source.Status);
            Assert.Equal(Source.Target, source.Target);
            Assert.Equal(ExpectedExpiry, source.Result!.ExpiresAt);
            Assert.False(source.Result.Success);
            Assert.Equal(Source.Error, source.Error);
            Assert.Null(source.CompletedAt);
        }

        public async Task AssertQuarantinedAsync()
        {
            RecoveryRecord source = (await Store.FindAsync(Source.OperationId))!;
            Assert.Equal("unknown", source.Status);
            Assert.False(Operations.OperationReconciliationEvidence.IsResolved(source, await Store.ListAsync(limit: null)));
            Assert.Equal(PriorOperationId, (await RegistrationAsync()).LastVerifiedOperationId);
            Operations.SchedulerStatusDto scheduler = await SchedulerAsync();
            Assert.False(scheduler.Enabled);
            Assert.Equal("held", scheduler.Concurrency.LockState);
        }

        public void Dispose()
        {
            Client.Dispose();
            Factory.Dispose();
            Directory.Delete(RootDirectory, recursive: true);
        }
    }

    private sealed class RecoverySigningIdentity(string root, DateTimeOffset expiry) : ISigningIdentityProvider
    {
        public string State { get; set; } = "reusable";
        public List<bool> AllowCertificateCreation { get; } = [];
        public int LegacyPrepareCalls { get; private set; }
        public Func<Task>? OnPrepare { get; set; }

        public Task<SigningIdentityInspection> InspectAsync(string appleId, string teamId, CancellationToken ct = default) =>
            Task.FromResult(new SigningIdentityInspection(State, expiry, "FAKE"));

        public Task<PreparedSigningInputs> PrepareAsync(
            AppleSession session, string teamId, string bundleId, string deviceUdid, CancellationToken ct = default)
        {
            LegacyPrepareCalls++;
            throw new InvalidOperationException("Recovery must use the explicit certificate-creation policy overload.");
        }

        public async Task<PreparedSigningInputs> PrepareAsync(
            AppleSession session, string teamId, string bundleId, string deviceUdid,
            bool allowCertificateCreation, CancellationToken ct = default)
        {
            AllowCertificateCreation.Add(allowCertificateCreation);
            if (OnPrepare is not null)
                await OnPrepare();
            string directory = Path.Combine(root, "identity", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string p12 = Path.Combine(directory, "identity.p12");
            string profile = Path.Combine(directory, "profile.mobileprovision");
            await File.WriteAllBytesAsync(p12, new byte[] { 1, 2, 3 }, ct);
            await File.WriteAllBytesAsync(profile, new byte[] { 4, 5, 6 }, ct);
            return new PreparedSigningInputs(p12, "", profile, expiry);
        }
    }

    private sealed class RecoveryDeviceController(string bundleId, DateTimeOffset futureExpiry) : IDeviceController
    {
        private int _installCalls;
        private bool _installCompleted;
        public int InstallCalls => Volatile.Read(ref _installCalls);
        public int FreshReads { get; private set; }
        public int CachedReads { get; private set; }
        public int PairCalls { get; private set; }
        public bool Trusted { get; set; } = true;
        public bool Installed { get; set; } = true;
        public bool RejectAllReads { get; set; }
        public string Version { get; set; } = "1.0";
        public DateTimeOffset? ProfileExpiry { get; set; }
        public string Readback { get; set; } = "verified";
        public DeviceConnection? RequiredConnection { get; private set; }
        public Func<Task>? OnInstall { get; set; }

        public Task<IReadOnlyList<DeviceInfo>> ListDevicesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<DeviceInfo>>([new DeviceInfo(
                "TEST-UDID", "Recovery iPhone", "iPhone15,2", "18.5", DeviceConnection.Wifi,
                Trusted ? "trusted" : "locked", "Previously USB-paired Wi-Fi test device.",
                DateTimeOffset.UtcNow, UsableForInstall: Trusted)]);

        public Task<DeviceTrustProbe> ProbeTrustAsync(string udid, CancellationToken ct = default) =>
            Task.FromResult(new DeviceTrustProbe(
                udid, DeviceConnection.Wifi, Trusted ? "trusted" : "locked",
                "Previously USB-paired Wi-Fi test device.", DateTimeOffset.UtcNow, Trusted));

        public Task<DevicePairingResult> PairAsync(
            string udid, IProgress<DevicePairingProgress>? progress = null, CancellationToken ct = default)
        {
            PairCalls++;
            throw new InvalidOperationException("A refresh must not request pairing.");
        }

        public Task<IReadOnlyList<InstalledApp>> ListInstalledAppsAsync(string udid, CancellationToken ct = default)
        {
            CachedReads++;
            return ReadAppsAsync();
        }

        public Task<IReadOnlyList<InstalledApp>> ListInstalledAppsFreshAsync(string udid, CancellationToken ct = default)
        {
            FreshReads++;
            return ReadAppsAsync();
        }

        private Task<IReadOnlyList<InstalledApp>> ReadAppsAsync()
        {
            if (RejectAllReads || (_installCompleted && Readback == "unreadable"))
                throw new IOException("Simulated unavailable inventory.");
            if (!Installed || (_installCompleted && Readback == "missing-app"))
                return Task.FromResult<IReadOnlyList<InstalledApp>>([]);
            DateTimeOffset? observedExpiry = !_installCompleted ? ProfileExpiry : Readback switch
            {
                "missing-profile" => null,
                "wrong-expiry" => futureExpiry.AddHours(1),
                _ => futureExpiry,
            };
            string version = _installCompleted && Readback == "wrong-version" ? "2.0" : Version;
            return Task.FromResult<IReadOnlyList<InstalledApp>>([
                new InstalledApp(bundleId, "Recovery test", version, observedExpiry),
            ]);
        }

        public async Task InstallAsync(
            string udid, string ipaPath, CancellationToken ct = default, DeviceConnection? requiredConnection = null)
        {
            Interlocked.Increment(ref _installCalls);
            RequiredConnection = requiredConnection;
            Assert.Equal("TEST-UDID", udid);
            IpaInfo ipa = IpaInspector.Inspect(ipaPath);
            Assert.Equal(bundleId, ipa.BundleIdentifier);
            Assert.Equal("1.0", ipa.ShortVersion);
            if (OnInstall is not null)
                await OnInstall();
            if (Readback == "install-failure")
                throw new IOException("Simulated loss of response after installation started.");
            _installCompleted = true;
        }

        public Task<DeviceDiagnostics> DiagnoseAsync(CancellationToken ct = default) =>
            Task.FromResult(new DeviceDiagnostics("ok", []));
    }
}
