using System.Net;

namespace Sideport.DeveloperApi.DeveloperServices;

/// <summary>
/// An error from the Apple developer-services API. <see cref="ResultCode"/> is
/// Apple's <c>resultCode</c> (0 = success; non-zero values identify the failure,
/// e.g. 9401 = bundle identifier unavailable).
/// </summary>
public sealed class DeveloperServicesException : Exception
{
    public DeveloperServicesException(
        string message,
        long resultCode = 0,
        HttpStatusCode? statusCode = null)
        : base(message)
    {
        ResultCode = resultCode;
        StatusCode = statusCode;
    }

    public DeveloperServicesException(
        string message,
        Exception innerException,
        HttpStatusCode? statusCode = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    /// <summary>Apple's <c>resultCode</c> (0 when the failure is not a coded API error).</summary>
    public long ResultCode { get; }

    /// <summary>The HTTP response status, when Apple returned a non-success response.</summary>
    public HttpStatusCode? StatusCode { get; }
}
