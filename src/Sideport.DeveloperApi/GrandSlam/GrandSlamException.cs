using System.Net;

namespace Sideport.DeveloperApi.GrandSlam;

/// <summary>
/// A GrandSlam protocol error: the server returned a non-zero status code
/// (<c>Status.ec</c>) with a message (<c>Status.em</c>), or the response was
/// otherwise unusable. <see cref="ErrorCode"/> is <c>null</c> for transport- or
/// parse-level failures that did not carry an Apple error code.
/// </summary>
public sealed class GrandSlamException : Exception
{
    /// <summary>The Apple error code (<c>Status.ec</c>), if the server supplied one.</summary>
    public long? ErrorCode { get; }

    /// <summary>The HTTP response status, when Apple returned a non-success response.</summary>
    public HttpStatusCode? StatusCode { get; }

    public GrandSlamException(
        string message,
        long? errorCode = null,
        HttpStatusCode? statusCode = null)
        : base(message)
    {
        ErrorCode = errorCode;
        StatusCode = statusCode;
    }

    public GrandSlamException(
        string message,
        Exception innerException,
        HttpStatusCode? statusCode = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
