using Sideport.Api.Operations;

namespace Sideport.Api.DiagnosticsIssues;

public sealed class DiagnosticIssueService(OperationStore operations, DiagnosticIssueStore store)
{
    private static readonly string[] ValidStatuses = ["unresolved", "investigating", "resolved", "ignored"];

    public async Task<IReadOnlyList<DiagnosticIssueDto>> ListAsync(CancellationToken ct = default)
    {
        IReadOnlyDictionary<string, DiagnosticIssueState> states = await store.ListStatesAsync(ct).ConfigureAwait(false);
        IReadOnlyList<OperationRecordDto> records = await operations.ListAsync(limit: null, ct: ct).ConfigureAwait(false);
        return BuildIssues(records, states).ToArray();
    }

    public async Task<DiagnosticIssueDto?> FindAsync(string issueId, CancellationToken ct = default) =>
        (await ListAsync(ct).ConfigureAwait(false)).FirstOrDefault(issue => string.Equals(issue.IssueId, issueId, StringComparison.Ordinal));

    public async Task<DiagnosticIssueDto?> PatchAsync(string issueId, DiagnosticIssuePatchRequest request, CancellationToken ct = default)
    {
        string status = NormalizeStatus(request.Status);
        DiagnosticIssueDto? issue = await FindAsync(issueId, ct).ConfigureAwait(false);
        if (issue is null)
            return null;

        await store.UpsertAsync(new DiagnosticIssueState(issueId, status, NormalizeOptional(request.Note), DateTimeOffset.UtcNow), ct).ConfigureAwait(false);
        return await FindAsync(issueId, ct).ConfigureAwait(false);
    }

    private static IEnumerable<DiagnosticIssueDto> BuildIssues(IReadOnlyList<OperationRecordDto> records, IReadOnlyDictionary<string, DiagnosticIssueState> states)
    {
        var failed = records
            .Where(record => record.Status is "failed" or "blocked")
            .Where(record => record.Error is not null)
            .GroupBy(record => IssueId(record), StringComparer.Ordinal)
            .OrderByDescending(group => group.Max(record => record.CompletedAt ?? record.UpdatedAt));

        foreach (IGrouping<string, OperationRecordDto> group in failed)
        {
            OperationRecordDto latest = group.OrderByDescending(record => record.CompletedAt ?? record.UpdatedAt).First();
            DateTimeOffset firstSeen = group.Min(record => record.CreatedAt);
            DateTimeOffset lastSeen = group.Max(record => record.CompletedAt ?? record.UpdatedAt);
            DiagnosticIssueState? state = states.GetValueOrDefault(group.Key);
            string status = state is null || (state.Status == "resolved" && state.UpdatedAt < lastSeen) ? "unresolved" : state.Status;
            OperationIssueDto error = latest.Error!;
            OperationStageDto? failedStage = latest.Stages.LastOrDefault(stage => stage.Error is not null || stage.Status is "failed" or "blocked");
            yield return new DiagnosticIssueDto(
                group.Key,
                error.Code,
                SeverityFor(error.Code),
                status,
                new DiagnosticAffectedDto(latest.Target.DeviceUdid, latest.Target.BundleId),
                firstSeen,
                lastSeen,
                group.Count(),
                latest.OperationId,
                latest.CorrelationId,
                [new DiagnosticEvidenceDto(
                    failedStage is null ? "operation" : "operation-stage",
                    failedStage?.Label ?? latest.Type,
                    Redact(failedStage?.Error?.Message ?? error.Message),
                    "live",
                    latest.OperationId,
                    failedStage?.Id)],
                RemediationFor(error.Code));
        }
    }

    private static string IssueId(OperationRecordDto record)
    {
        string errorCode = record.Error?.Code ?? "operation-failed";
        return $"issue-{Slug(errorCode)}-{Slug(record.Target.DeviceUdid ?? record.Target.Kind ?? "unknown-target")}-{Slug(record.Target.BundleId ?? record.Type)}";
    }

    private static string NormalizeStatus(string status)
    {
        if (string.IsNullOrWhiteSpace(status) || !ValidStatuses.Contains(status.Trim(), StringComparer.Ordinal))
            throw new ArgumentException("Diagnostic issue status must be unresolved, investigating, resolved, or ignored.", nameof(status));
        return status.Trim();
    }

    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string SeverityFor(string code) => code switch
    {
        "registration-missing" => "warning",
        "operation-terminal-state-unknown" => "fatal",
        _ => "error",
    };

    private static string RemediationFor(string code) => code switch
    {
        "registration-missing" => "Register the app on this device before running refresh.",
        "refresh-failed" => "Review the failed operation stage, fix the blocker, then retry after preflight.",
        "operation-terminal-state-unknown" => "Inspect the device state before retrying; the previous run may have partially completed.",
        _ => "Review the linked operation evidence and rerun preflight before retrying.",
    };

    private static string Redact(string value)
    {
        string redacted = value;
        redacted = System.Text.RegularExpressions.Regex.Replace(redacted, @"(?i)(password|token|secret|key)\s*[:=]\s*\S+", "$1=[redacted]");
        redacted = System.Text.RegularExpressions.Regex.Replace(redacted, @"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}", "[redacted-email]");
        redacted = System.Text.RegularExpressions.Regex.Replace(redacted, @"/[^\s:]+", "[redacted-path]");
        if (redacted.Length > 500)
            redacted = string.Concat(redacted.AsSpan(0, 500), "...");
        return redacted;
    }

    private static string Slug(string value)
    {
        var chars = value.ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray();
        return string.Join('-', new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
    }
}
