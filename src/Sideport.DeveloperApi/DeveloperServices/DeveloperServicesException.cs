namespace Sideport.DeveloperApi.DeveloperServices;

/// <summary>
/// An error from the Apple developer-services API. <see cref="ResultCode"/> is
/// Apple's <c>resultCode</c> (0 = success; non-zero values identify the failure,
/// e.g. 9401 = bundle identifier unavailable).
/// </summary>
public sealed class DeveloperServicesException : Exception
{
    public DeveloperServicesException(string message, long resultCode = 0)
        : base(message)
    {
        ResultCode = resultCode;
    }

    /// <summary>Apple's <c>resultCode</c> (0 when the failure is not a coded API error).</summary>
    public long ResultCode { get; }
}
