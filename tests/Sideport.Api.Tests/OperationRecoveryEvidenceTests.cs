using Sideport.Api.Operations;
using System.Text.Json;

namespace Sideport.Api.Tests;

public sealed class OperationRecoveryEvidenceTests
{
    [Fact]
    public async Task RecoveryHistory_UsesGuardedEnvelopeWithoutLosingLegacyRecords()
    {
        Evidence evidence = CreateEvidence();
        string directory = Path.Combine(Path.GetTempPath(), "sideport-schema-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "operations.json");
            var store = new OperationStore(path);
            await store.AddIfIdempotentMissingAsync(evidence.Source);
            using (JsonDocument legacy = JsonDocument.Parse(await File.ReadAllTextAsync(path)))
                Assert.Equal(JsonValueKind.Array, legacy.RootElement.ValueKind);

            await store.AddIfIdempotentMissingAsync(evidence.Receipt);
            await store.AddIfIdempotentMissingAsync(evidence.Child);
            string persisted = await File.ReadAllTextAsync(path);
            using (JsonDocument current = JsonDocument.Parse(persisted))
            {
                Assert.Equal(2, current.RootElement.GetProperty("schemaVersion").GetInt32());
                Assert.Equal(3, current.RootElement.GetProperty("operations").GetArrayLength());
            }
            Assert.Throws<JsonException>(() =>
                JsonSerializer.Deserialize<List<OperationRecordDto>>(persisted, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

            var reopened = new OperationStore(path);
            OperationRecordDto restored = (await reopened.FindAsync(evidence.Child.OperationId))!;
            Assert.Equal(evidence.Child.RecoveryIntent, restored.RecoveryIntent);
            Assert.Equal(evidence.Child.RecoveryCheckpoint, restored.RecoveryCheckpoint);
            Assert.Equal("unknown", (await reopened.FindAsync(evidence.Source.OperationId))!.Status);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{\"schemaVersion\":3,\"operations\":[]}")]
    [InlineData("{\"schemaVersion\":\"2\",\"operations\":[]}")]
    [InlineData("{\"schemaVersion\":2,\"operations\":null}")]
    public async Task RecoveryBarrier_RefusesMalformedOrUnsupportedHistory(string data)
    {
        string directory = Path.Combine(Path.GetTempPath(), "sideport-schema-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "operations.json");
            await File.WriteAllTextAsync(path, data);
            await Assert.ThrowsAsync<OperationStoreException>(() =>
                new OperationRecoveryBarrier(new OperationStore(path)).StartAsync(CancellationToken.None));
            Assert.Equal(data, await File.ReadAllTextAsync(path));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task RecoveryBarrier_RetainsPreparedExpiryWhenProcessDiedBeforeResult()
    {
        Evidence evidence = CreateEvidence();
        string directory = Path.Combine(Path.GetTempPath(), "sideport-checkpoint-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "operations.json");
            var store = new OperationStore(path);
            OperationRecordDto interrupted = evidence.Child with
            {
                Status = "running",
                UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(1),
                CompletedAt = null,
                Result = null,
                Stages = [new OperationStageDto("renew", "Renew", "running", DateTimeOffset.UtcNow, null, "Installing.")],
            };
            await store.AddIfIdempotentMissingAsync(interrupted);
            var restarted = new OperationStore(path);
            await new OperationRecoveryBarrier(restarted).StartAsync(CancellationToken.None);
            OperationRecordDto restored = (await restarted.FindAsync(interrupted.OperationId))!;
            Assert.Equal("unknown", restored.Status);
            Assert.False(restored.Result!.Success);
            Assert.Equal(interrupted.RecoveryCheckpoint!.PreparedExpiresAt, restored.Result.ExpiresAt);
            Assert.Equal(interrupted.RecoveryCheckpoint, restored.RecoveryCheckpoint);
            Assert.False(restored.Rerunnable);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void RenewalEligibleObservation_IsNotClosureOrPermissionForOrdinaryRerun()
    {
        Evidence evidence = CreateEvidence();
        OperationRecordDto[] records = [evidence.Source, evidence.Receipt];

        Assert.False(OperationReconciliationEvidence.IsResolved(evidence.Source, records));
        Assert.False(OperationReconciliationEvidence.HasCompletedReconciliation(evidence.Source, records));
        Assert.True(OperationReconciliationEvidence.IsUnresolvedMutation(evidence.Source, records));
        Assert.True(OperationReconciliationEvidence.IsUnresolvedForManualAction(evidence.Source, records));
    }

    [Fact]
    public void VerifiedSuccessor_ClosesOnlyItsExactPredecessorWithoutRewritingUnknownHistory()
    {
        Evidence evidence = CreateEvidence();
        OperationRecordDto unrelated = evidence.Source with { OperationId = "op_other_unknown" };
        OperationRecordDto[] records = [evidence.Source, evidence.Receipt, evidence.Child, unrelated];

        Assert.True(OperationReconciliationEvidence.IsResolved(evidence.Source, records));
        Assert.False(OperationReconciliationEvidence.IsUnresolvedMutation(evidence.Source, records));
        Assert.False(OperationReconciliationEvidence.IsUnresolvedForManualAction(evidence.Source, records));
        Assert.False(OperationReconciliationEvidence.IsResolved(unrelated, records));
        Assert.True(OperationReconciliationEvidence.IsUnresolvedMutation(unrelated, records));
        Assert.Equal("unknown", evidence.Source.Status);
        Assert.False(evidence.Source.Result!.Success);
        Assert.Null(evidence.Source.CompletedAt);
    }

    [Theory]
    [InlineData("device")]
    [InlineData("bundle")]
    [InlineData("team")]
    [InlineData("account")]
    [InlineData("version")]
    [InlineData("hash")]
    [InlineData("catalog-app")]
    [InlineData("catalog-version")]
    [InlineData("missing-device")]
    [InlineData("missing-bundle")]
    [InlineData("missing-team")]
    [InlineData("missing-account")]
    [InlineData("missing-version")]
    [InlineData("missing-hash")]
    [InlineData("missing-catalog-app")]
    [InlineData("missing-catalog-version")]
    public void VerifiedSuccessor_RejectsEveryChangedOrMissingTargetLineageField(string mutation)
    {
        Evidence evidence = CreateEvidence();
        OperationTargetDto target = evidence.Child.Target;
        OperationTargetDto changed = mutation switch
        {
            "device" => target with { DeviceUdid = "OTHER-DEVICE" },
            "bundle" => target with { BundleId = "com.example.other" },
            "team" => target with { TeamId = "OTHER-TEAM" },
            "account" => target with { AccountProfileId = "other-account" },
            "version" => target with { Version = "2.0" },
            "hash" => target with { CatalogSha256 = new string('b', 64) },
            "catalog-app" => target with { CatalogAppId = "other-app" },
            "catalog-version" => target with { CatalogVersion = 2 },
            "missing-device" => target with { DeviceUdid = null },
            "missing-bundle" => target with { BundleId = null },
            "missing-team" => target with { TeamId = null },
            "missing-account" => target with { AccountProfileId = null },
            "missing-version" => target with { Version = null },
            "missing-hash" => target with { CatalogSha256 = null },
            "missing-catalog-app" => target with { CatalogAppId = null },
            "missing-catalog-version" => target with { CatalogVersion = null },
            _ => throw new ArgumentOutOfRangeException(nameof(mutation)),
        };

        AssertNotClosed(evidence, evidence.Child with { Target = changed });
    }

    [Theory]
    [InlineData("not-successful")]
    [InlineData("missing-result")]
    [InlineData("missing-result-version")]
    [InlineData("wrong-result-version")]
    [InlineData("wrong-result-bundle")]
    [InlineData("missing-result-expiry")]
    [InlineData("wrong-result-expiry")]
    [InlineData("missing-checkpoint")]
    [InlineData("wrong-checkpoint-hash")]
    [InlineData("missing-verify-stage")]
    [InlineData("verify-not-succeeded")]
    [InlineData("verify-not-completed")]
    [InlineData("not-terminal")]
    [InlineData("wrong-operation-type")]
    public void VerifiedSuccessor_RequiresDurableExactVerificationNotJustSucceededStatus(string mutation)
    {
        Evidence evidence = CreateEvidence();
        OperationRecordDto child = evidence.Child;
        OperationRecordDto changed = mutation switch
        {
            "not-successful" => child with { Result = child.Result! with { Success = false } },
            "missing-result" => child with { Result = null },
            "missing-result-version" => child with { Result = child.Result! with { Version = null } },
            "wrong-result-version" => child with { Result = child.Result! with { Version = "2.0" } },
            "wrong-result-bundle" => child with { Result = child.Result! with { BundleId = "com.example.other" } },
            "missing-result-expiry" => child with { Result = child.Result! with { ExpiresAt = null } },
            "wrong-result-expiry" => child with { Result = child.Result! with { ExpiresAt = child.Result.ExpiresAt!.Value.AddHours(1) } },
            "missing-checkpoint" => child with { RecoveryCheckpoint = null },
            "wrong-checkpoint-hash" => child with
            {
                RecoveryCheckpoint = child.RecoveryCheckpoint! with { PinnedArtifactSha256 = new string('b', 64) },
            },
            "missing-verify-stage" => child with { Stages = [] },
            "verify-not-succeeded" => child with
            {
                Stages = child.Stages.Select(stage => stage with { Status = "running" }).ToArray(),
            },
            "verify-not-completed" => child with
            {
                Stages = child.Stages.Select(stage => stage with { CompletedAt = null }).ToArray(),
            },
            "not-terminal" => child with { Status = "running" },
            "wrong-operation-type" => child with { Type = "verify-existing-registration" },
            _ => throw new ArgumentOutOfRangeException(nameof(mutation)),
        };

        AssertNotClosed(evidence, changed);
    }

    [Theory]
    [InlineData("self")]
    [InlineData("parent-cycle")]
    [InlineData("other-parent")]
    [InlineData("intent-predecessor")]
    [InlineData("intent-kind")]
    [InlineData("missing-receipt")]
    [InlineData("intent-device")]
    [InlineData("intent-bundle")]
    [InlineData("intent-version")]
    [InlineData("intent-team")]
    [InlineData("intent-account")]
    [InlineData("intent-hash")]
    [InlineData("intent-expiry")]
    [InlineData("intent-catalog")]
    public void VerifiedSuccessor_RejectsForgedRecoveryIntentAndCycles(string mutation)
    {
        Evidence evidence = CreateEvidence();
        OperationRecordDto child = evidence.Child;
        OperationRecoveryIntentDto intent = child.RecoveryIntent!;
        OperationRecordDto changed = mutation switch
        {
            "self" => child with { OperationId = evidence.Source.OperationId },
            "parent-cycle" => child with { ParentOperationId = child.OperationId },
            "other-parent" => child with { ParentOperationId = "op_other_parent" },
            "intent-predecessor" => child with { RecoveryIntent = intent with { PredecessorOperationId = "op_other_parent" } },
            "intent-kind" => child with { RecoveryIntent = intent with { Kind = "automatic-retry" } },
            "missing-receipt" => child with { RecoveryIntent = intent with { ReceiptOperationId = "" } },
            "intent-device" => child with { RecoveryIntent = intent with { DeviceUdid = "OTHER-DEVICE" } },
            "intent-bundle" => child with { RecoveryIntent = intent with { BundleId = "com.example.other" } },
            "intent-version" => child with { RecoveryIntent = intent with { Version = "2.0" } },
            "intent-team" => child with { RecoveryIntent = intent with { TeamId = "OTHER-TEAM" } },
            "intent-account" => child with { RecoveryIntent = intent with { AccountProfileId = "other-account" } },
            "intent-hash" => child with { RecoveryIntent = intent with { CatalogSha256 = new string('b', 64) } },
            "intent-expiry" => child with
            {
                RecoveryIntent = intent with { PredecessorExpectedExpiresAt = intent.PredecessorExpectedExpiresAt.AddHours(1) },
            },
            "intent-catalog" => child with { RecoveryIntent = intent with { CatalogVersion = 2 } },
            _ => throw new ArgumentOutOfRangeException(nameof(mutation)),
        };

        AssertNotClosed(evidence, changed);
    }

    [Fact]
    public void OrdinarySuccessor_DoesNotCloseUnknownWithoutLinkedSafeToRerunReceipt()
    {
        Evidence evidence = CreateEvidence();
        OperationRecordDto ordinary = AsOrdinaryRerun(evidence.Child);

        Assert.False(OperationReconciliationEvidence.IsResolved(evidence.Source, [evidence.Source, ordinary]));
        Assert.False(OperationReconciliationEvidence.IsResolved(
            evidence.Source, [evidence.Source, evidence.Receipt, ordinary]));
    }

    [Fact]
    public void OrdinaryRerun_AfterAbsentAppReceiptAndVerifiedRefresh_ClosesExactSource()
    {
        Evidence evidence = CreateEvidence();
        OperationRecordDto receipt = evidence.Receipt with
        {
            Result = evidence.Receipt.Result! with { SafeToRerun = true, RenewalEligible = false },
        };
        OperationRecordDto ordinary = AsOrdinaryRerun(evidence.Child);

        Assert.False(OperationReconciliationEvidence.IsResolved(evidence.Source, [evidence.Source, receipt]));
        Assert.True(OperationReconciliationEvidence.IsResolved(evidence.Source, [evidence.Source, receipt, ordinary]));
        Assert.False(OperationReconciliationEvidence.IsUnresolvedMutation(
            evidence.Source, [evidence.Source, receipt, ordinary]));
    }

    [Theory]
    [InlineData("parent")]
    [InlineData("reconciled-source")]
    [InlineData("target")]
    [InlineData("receipt-not-succeeded")]
    [InlineData("refresh-result-not-successful")]
    public void OrdinaryRerun_RejectsUnrelatedOrUnverifiedAbsenceEvidence(string mutation)
    {
        Evidence evidence = CreateEvidence();
        OperationRecordDto receipt = evidence.Receipt with
        {
            Result = evidence.Receipt.Result! with { SafeToRerun = true, RenewalEligible = false },
        };
        OperationRecordDto ordinary = AsOrdinaryRerun(evidence.Child);
        switch (mutation)
        {
            case "parent":
                receipt = receipt with { ParentOperationId = "op_other_unknown" };
                break;
            case "reconciled-source":
                receipt = receipt with { Result = receipt.Result! with { ReconciledOperationId = "op_other_unknown" } };
                break;
            case "target":
                receipt = receipt with { Target = receipt.Target with { DeviceUdid = "OTHER-DEVICE" } };
                break;
            case "receipt-not-succeeded":
                receipt = receipt with { Status = "blocked" };
                break;
            case "refresh-result-not-successful":
                ordinary = ordinary with { Result = ordinary.Result! with { Success = false } };
                break;
        }

        Assert.False(OperationReconciliationEvidence.IsResolved(evidence.Source, [evidence.Source, receipt, ordinary]));
    }

    [Fact]
    public async Task VerifiedSuccessorClosure_SurvivesStoreReloadAndLaterSignatureExpiry()
    {
        Evidence evidence = CreateEvidence();
        DateTimeOffset completion = DateTimeOffset.UtcNow.AddDays(-3);
        DateTimeOffset oldExpiry = DateTimeOffset.UtcNow.AddDays(-1);
        OperationRecordDto child = evidence.Child with
        {
            CreatedAt = completion.AddMinutes(-1),
            StartedAt = completion.AddMinutes(-1),
            UpdatedAt = completion,
            CompletedAt = completion,
            Result = evidence.Child.Result! with { ExpiresAt = oldExpiry },
            RecoveryCheckpoint = evidence.Child.RecoveryCheckpoint! with
            {
                PreparedExpiresAt = oldExpiry,
                MutationStartedAt = completion.AddSeconds(-1),
            },
            Stages = evidence.Child.Stages.Select(stage => stage with
            {
                StartedAt = completion.AddSeconds(-1), CompletedAt = completion,
            }).ToArray(),
        };
        OperationRecordDto receipt = evidence.Receipt with
        {
            CreatedAt = completion.AddMinutes(-2),
            StartedAt = completion.AddMinutes(-2),
            UpdatedAt = completion.AddMinutes(-1),
            CompletedAt = completion.AddMinutes(-1),
            Stages = evidence.Receipt.Stages.Select(stage => stage with
            {
                StartedAt = completion.AddMinutes(-2), CompletedAt = completion.AddMinutes(-1),
            }).ToArray(),
        };
        string directory = Path.Combine(Directory.GetCurrentDirectory(), ".recovery-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "operations.json");
            var store = new OperationStore(path);
            foreach (OperationRecordDto record in new[] { evidence.Source, receipt, child })
                await store.AddIfIdempotentMissingAsync(record);

            var reopened = new OperationStore(path);
            await new OperationRecoveryBarrier(reopened).StartAsync(CancellationToken.None);
            IReadOnlyList<OperationRecordDto> records = await reopened.ListAsync(limit: null);
            OperationRecordDto source = (await reopened.FindAsync(evidence.Source.OperationId))!;
            Assert.Equal("unknown", source.Status);
            Assert.Equal(evidence.Source.Result!.ExpiresAt, source.Result!.ExpiresAt);
            Assert.True(OperationReconciliationEvidence.IsResolved(source, records));
            Assert.False(OperationReconciliationEvidence.IsUnresolvedMutation(source, records));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void AssertNotClosed(Evidence evidence, OperationRecordDto child)
    {
        OperationRecordDto[] records = [evidence.Source, evidence.Receipt, child];
        Assert.False(OperationReconciliationEvidence.IsResolved(evidence.Source, records));
        Assert.True(OperationReconciliationEvidence.IsUnresolvedMutation(evidence.Source, records));
        Assert.True(OperationReconciliationEvidence.IsUnresolvedForManualAction(evidence.Source, records));
    }

    private static Evidence CreateEvidence()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset expectedExpiry = now.AddDays(-7);
        DateTimeOffset futureExpiry = now.AddDays(7);
        string suffix = Guid.NewGuid().ToString("N");
        string sourceId = $"op_source_{suffix}";
        string receiptId = $"op_receipt_{suffix}";
        string childId = $"op_child_{suffix}";
        var target = new OperationTargetDto(
            "TEST-UDID",
            "com.example.recovery",
            TeamId: "TEST-TEAM",
            Kind: "app",
            CatalogAppId: "test-catalog-app",
            AccountProfileId: "test-account",
            CatalogVersion: 1,
            Version: "1.0",
            CatalogSha256: new string('a', 64));
        var source = new OperationRecordDto(
            sourceId, "refresh", "unknown", now.AddDays(-8), now.AddDays(-8), now.AddDays(-8), null,
            new OperationActorDto("recovery-bearer", "Recovery test"),
            $"source-key-{suffix}", 1, target, [],
            new OperationResultDto(false, target.BundleId, expectedExpiry, "Outcome unknown."),
            new OperationIssueDto("install-outcome-unknown", "Outcome unknown."),
            false, false, false, sourceId);
        var receipt = source with
        {
            OperationId = receiptId,
            Type = "reconcile",
            Status = "succeeded",
            CreatedAt = now.AddMinutes(-1),
            StartedAt = now.AddMinutes(-1),
            UpdatedAt = now.AddMinutes(-1),
            CompletedAt = now.AddMinutes(-1),
            IdempotencyKey = $"receipt-key-{suffix}",
            ParentOperationId = sourceId,
            CorrelationId = receiptId,
            Result = new OperationResultDto(
                false, target.BundleId, null, null, Version: target.Version,
                SafeToRerun: false, ReconciledOperationId: sourceId, RenewalEligible: true),
            Error = null,
            Stages = [new OperationStageDto(
                "verify", "Observe iPhone", "succeeded",
                now.AddMinutes(-1), now.AddMinutes(-1), "Exact version; profile unavailable.")],
        };
        var child = source with
        {
            OperationId = childId,
            Status = "succeeded",
            CreatedAt = now.AddSeconds(-10),
            StartedAt = now.AddSeconds(-10),
            UpdatedAt = now,
            CompletedAt = now,
            IdempotencyKey = $"child-key-{suffix}",
            ParentOperationId = sourceId,
            CorrelationId = childId,
            Result = new OperationResultDto(true, target.BundleId, futureExpiry, null, Version: target.Version),
            Error = null,
            Stages =
            [
                new OperationStageDto(
                    "verify", "Verify iPhone", "succeeded", now.AddSeconds(-1), now, "Exact installed renewal verified."),
                new OperationStageDto(
                    "activate-registration", "Save verification", "succeeded", now, now, "Verified registration saved."),
            ],
            RecoveryIntent = new OperationRecoveryIntentDto(
                "superseding-renewal", receiptId, sourceId, target.DeviceUdid!, target.BundleId!,
                target.TeamId!, target.AccountProfileId!, target.Version!, target.CatalogSha256!,
                expectedExpiry, target.CatalogVersion, target.CatalogAppId),
            RecoveryCheckpoint = new OperationRecoveryCheckpointDto(
                futureExpiry, target.CatalogSha256!, now.AddSeconds(-2)),
        };
        return new Evidence(source, receipt, child);
    }

    private sealed record Evidence(OperationRecordDto Source, OperationRecordDto Receipt, OperationRecordDto Child);

    private static OperationRecordDto AsOrdinaryRerun(OperationRecordDto child) => child with
    {
        RecoveryIntent = null,
        RecoveryCheckpoint = null,
        Stages = child.Stages.Where(stage => stage.Id == "verify")
            .Select(stage => stage with { Id = "refresh" }).ToArray(),
    };
}
