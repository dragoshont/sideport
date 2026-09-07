namespace Sideport.Api.Operations;

internal static class OperationReconciliationEvidence
{
    public const string OperationType = "reconcile";
    public const string SupersedingRenewalKind = "superseding-renewal";

    public static bool IsResolved(
        OperationRecordDto source,
        IReadOnlyList<OperationRecordDto> records) =>
        IsResolved(source, records, new HashSet<string>(StringComparer.Ordinal));

    private static bool IsResolved(
        OperationRecordDto source,
        IReadOnlyList<OperationRecordDto> records,
        HashSet<string> visited)
    {
        if (visited.Count >= 128 || !visited.Add(source.OperationId))
            return false;
        try
        {
            if (records.Any(candidate => IsVerifiedReconcileClosure(candidate, source)))
                return true;

            foreach (OperationRecordDto child in records)
            {
                if (!IsSuccessorLink(child, source) || !HasAuthorizingReceipt(child, source, records))
                    continue;
                if (IsVerifiedSuccessorClosure(child, source))
                    return true;
                if (child.Status is "unknown" or "recovery-required" &&
                    child.RecoveryCheckpoint is not null &&
                    IsResolved(child, records, visited))
                    return true;
            }
            return false;
        }
        finally
        {
            visited.Remove(source.OperationId);
        }
    }

    private static bool IsVerifiedReconcileClosure(OperationRecordDto candidate, OperationRecordDto source) =>
        candidate.OperationId != source.OperationId &&
        candidate.Type == OperationType && candidate.Status == "succeeded" &&
        candidate.ParentOperationId == source.OperationId &&
        candidate.Result is { Success: true, ExpiresAt: not null, Version.Length: > 0 } result &&
        result.ReconciledOperationId == source.OperationId &&
        SameLineage(candidate.Target, source.Target) &&
        result.Version == source.Target.Version &&
        source.Result?.ExpiresAt is { } expectedExpiry &&
        Math.Abs((result.ExpiresAt.Value - expectedExpiry).TotalSeconds) <= 60 &&
        HasCompletedStage(candidate, "verify");

    public static bool IsVerifiedSuccessorClosure(OperationRecordDto candidate, OperationRecordDto source) =>
        IsSuccessorLink(candidate, source) &&
        candidate.Status == "succeeded" && candidate.CompletedAt is not null &&
        candidate.Result is { Success: true, ExpiresAt: not null, Version.Length: > 0 } result &&
        result.Version == candidate.Target.Version &&
        result.BundleId == candidate.Target.BundleId &&
        (candidate.RecoveryIntent is null
            ? HasCompletedStage(candidate, candidate.Type == "refresh" ? "refresh" : "verify")
            : candidate.RecoveryCheckpoint is { } checkpoint &&
              string.Equals(checkpoint.PinnedArtifactSha256, candidate.Target.CatalogSha256, StringComparison.OrdinalIgnoreCase) &&
              Math.Abs((result.ExpiresAt.Value - checkpoint.PreparedExpiresAt).TotalSeconds) <= 60 &&
              HasCompletedStage(candidate, "verify") &&
              HasCompletedStage(candidate, "activate-registration"));

    internal static bool IsSuccessorLink(OperationRecordDto candidate, OperationRecordDto source) =>
        candidate.OperationId != source.OperationId &&
        candidate.Type is "refresh" or "install" &&
        candidate.ParentOperationId == source.OperationId &&
        candidate.OwnerMemberId == source.OwnerMemberId &&
        SameLineage(candidate.Target, source.Target) &&
        (candidate.RecoveryIntent is null ||
         candidate.RecoveryIntent.Kind == SupersedingRenewalKind &&
         candidate.RecoveryIntent.PredecessorOperationId == source.OperationId &&
         !string.IsNullOrWhiteSpace(candidate.RecoveryIntent.ReceiptOperationId) &&
         string.Equals(candidate.RecoveryIntent.DeviceUdid, source.Target.DeviceUdid, StringComparison.OrdinalIgnoreCase) &&
         candidate.RecoveryIntent.BundleId == source.Target.BundleId &&
         candidate.RecoveryIntent.TeamId == source.Target.TeamId &&
         candidate.RecoveryIntent.AccountProfileId == source.Target.AccountProfileId &&
         candidate.RecoveryIntent.Version == source.Target.Version &&
         source.Result?.ExpiresAt is { } expectedExpiry &&
         candidate.RecoveryIntent.PredecessorExpectedExpiresAt == expectedExpiry &&
         string.Equals(candidate.RecoveryIntent.CatalogSha256, source.Target.CatalogSha256, StringComparison.OrdinalIgnoreCase) &&
         candidate.RecoveryIntent.CatalogVersion == source.Target.CatalogVersion &&
         string.Equals(candidate.RecoveryIntent.CatalogAppId, source.Target.CatalogAppId, StringComparison.OrdinalIgnoreCase));

    private static bool HasAuthorizingReceipt(
        OperationRecordDto candidate,
        OperationRecordDto source,
        IReadOnlyList<OperationRecordDto> records) =>
        records.Any(receipt =>
            receipt.Type == OperationType && receipt.Status == "succeeded" &&
            receipt.ParentOperationId == source.OperationId &&
            receipt.OwnerMemberId == source.OwnerMemberId &&
            receipt.Result?.ReconciledOperationId == source.OperationId &&
            receipt.Result.Success == false &&
            SameLineage(receipt.Target, source.Target) &&
            HasCompletedStage(receipt, "verify") &&
            (candidate.RecoveryIntent is { } intent
                ? receipt.OperationId == intent.ReceiptOperationId && receipt.Result.RenewalEligible == true
                : receipt.Result.SafeToRerun == true));

    internal static bool HasVerifiedRecoveryEvidence(OperationRecordDto record) =>
        record.Type == "refresh" &&
        record.RecoveryIntent is { Kind: SupersedingRenewalKind } intent &&
        record.ParentOperationId == intent.PredecessorOperationId &&
        record.RecoveryCheckpoint is { } checkpoint &&
        record.Result is { Success: true, ExpiresAt: not null, Version.Length: > 0 } result &&
        result.BundleId == intent.BundleId && result.Version == intent.Version &&
        record.Target.BundleId == intent.BundleId &&
        string.Equals(record.Target.DeviceUdid, intent.DeviceUdid, StringComparison.OrdinalIgnoreCase) &&
        record.Target.TeamId == intent.TeamId && record.Target.AccountProfileId == intent.AccountProfileId &&
        record.Target.Version == intent.Version &&
        string.Equals(record.Target.CatalogSha256, intent.CatalogSha256, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(checkpoint.PinnedArtifactSha256, intent.CatalogSha256, StringComparison.OrdinalIgnoreCase) &&
        Math.Abs((result.ExpiresAt.Value - checkpoint.PreparedExpiresAt).TotalSeconds) <= 60 &&
        HasCompletedStage(record, "verify");

    private static bool SameLineage(OperationTargetDto left, OperationTargetDto right) =>
        !string.IsNullOrWhiteSpace(right.DeviceUdid) &&
        !string.IsNullOrWhiteSpace(right.BundleId) &&
        !string.IsNullOrWhiteSpace(right.Version) &&
        !string.IsNullOrWhiteSpace(right.CatalogSha256) &&
        string.Equals(left.DeviceUdid, right.DeviceUdid, StringComparison.OrdinalIgnoreCase) &&
        left.BundleId == right.BundleId && left.TeamId == right.TeamId &&
        left.AccountProfileId == right.AccountProfileId && left.Version == right.Version &&
        string.Equals(left.CatalogSha256, right.CatalogSha256, StringComparison.OrdinalIgnoreCase) &&
        left.CatalogVersion == right.CatalogVersion &&
        string.Equals(left.CatalogAppId, right.CatalogAppId, StringComparison.OrdinalIgnoreCase);

    private static bool HasCompletedStage(OperationRecordDto record, string id) =>
        record.Stages.Any(stage => stage.Id == id && stage.Status == "succeeded" && stage.CompletedAt is not null);

    public static bool HasCompletedReconciliation(
        OperationRecordDto source,
        IReadOnlyList<OperationRecordDto> records) =>
        IsResolved(source, records) || records.Any(candidate =>
            candidate.Type == OperationType && candidate.Status == "succeeded" &&
            candidate.ParentOperationId == source.OperationId &&
            candidate.Result?.ReconciledOperationId == source.OperationId &&
            candidate.Result.SafeToRerun == true &&
            SameLineage(candidate.Target, source.Target) &&
            HasCompletedStage(candidate, "verify"));

    public static bool IsUnresolvedMutation(OperationRecordDto operation, IReadOnlyList<OperationRecordDto> records) =>
        operation.Status is "unknown" or "recovery-required" &&
        operation.Type is "install" or "refresh" &&
        !IsResolved(operation, records);

    public static bool IsUnresolvedForManualAction(OperationRecordDto operation, IReadOnlyList<OperationRecordDto> records) =>
        operation.Status is "unknown" or "recovery-required" &&
        operation.Type is "install" or "refresh" &&
        !HasCompletedReconciliation(operation, records);
}
