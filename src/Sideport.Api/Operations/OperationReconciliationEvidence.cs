namespace Sideport.Api.Operations;

internal static class OperationReconciliationEvidence
{
    public const string OperationType = "reconcile";

    public static bool IsResolved(
        OperationRecordDto source,
        IReadOnlyList<OperationRecordDto> records) =>
        records.Any(candidate =>
            string.Equals(candidate.Type, OperationType, StringComparison.Ordinal) &&
            string.Equals(candidate.Status, "succeeded", StringComparison.Ordinal) &&
            string.Equals(candidate.ParentOperationId, source.OperationId, StringComparison.Ordinal) &&
            string.Equals(candidate.Result?.ReconciledOperationId, source.OperationId, StringComparison.Ordinal) &&
            candidate.Result?.Success == true);

    public static bool HasCompletedReconciliation(
        OperationRecordDto source,
        IReadOnlyList<OperationRecordDto> records) =>
        IsResolved(source, records) || records.Any(candidate =>
            string.Equals(candidate.Type, OperationType, StringComparison.Ordinal) &&
            string.Equals(candidate.Status, "succeeded", StringComparison.Ordinal) &&
            string.Equals(candidate.ParentOperationId, source.OperationId, StringComparison.Ordinal) &&
            string.Equals(candidate.Result?.ReconciledOperationId, source.OperationId, StringComparison.Ordinal) &&
            candidate.Result?.SafeToRerun == true);

    public static bool IsUnresolvedMutation(
        OperationRecordDto operation,
        IReadOnlyList<OperationRecordDto> records) =>
        operation.Status is "unknown" or "recovery-required" &&
        operation.Type is "install" or "refresh" &&
        !IsResolved(operation, records);

    public static bool IsUnresolvedForManualAction(
        OperationRecordDto operation,
        IReadOnlyList<OperationRecordDto> records) =>
        operation.Status is "unknown" or "recovery-required" &&
        operation.Type is "install" or "refresh" &&
        !HasCompletedReconciliation(operation, records);
}
