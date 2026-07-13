using Sideport.Api.Operations;

namespace Sideport.Api.DeviceInventory;

public sealed record DeviceEnrollmentRequest(
    string IdempotencyKey,
    string? DeviceUdid = null,
    string? TargetMemberId = null);

public sealed record DeviceEnrollmentSubmissionResult(
    OperationRecordDto? Record,
    bool Created,
    string? Error,
    string? Message = null);

public sealed class DeviceEnrollmentOptions
{
    public TimeSpan SessionTimeout { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(1);

    internal void Validate()
    {
        if (SessionTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(SessionTimeout));
        if (PollInterval <= TimeSpan.Zero || PollInterval > SessionTimeout)
            throw new ArgumentOutOfRangeException(nameof(PollInterval));
    }
}
