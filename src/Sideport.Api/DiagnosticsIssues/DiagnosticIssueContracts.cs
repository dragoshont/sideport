namespace Sideport.Api.DiagnosticsIssues;

public sealed record DiagnosticAffectedDto(string? DeviceUdid, string? BundleId);

public sealed record DiagnosticEvidenceDto(string Type, string Label, string Message, string Source, string? OperationId = null, string? StageId = null);

public sealed record DiagnosticIssueDto(
    string IssueId,
    string Category,
    string Severity,
    string Status,
    DiagnosticAffectedDto Affected,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    int OccurrenceCount,
    string? LastOperationId,
    string CorrelationId,
    IReadOnlyList<DiagnosticEvidenceDto> Evidence,
    string Remediation,
    string Source = "live");

public sealed record DiagnosticIssuePatchRequest(string Status, string? Note = null);

public sealed record DiagnosticIssueErrorDto(string Error, string Message, string? Detail = null);
