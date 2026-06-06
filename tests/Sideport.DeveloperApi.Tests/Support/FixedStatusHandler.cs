using System.Net;

namespace Sideport.DeveloperApi.Tests.Support;

/// <summary>
/// A fake handler that always returns a fixed status code with an arbitrary
/// (non-plist) body — models Apple's GSA 502/HTML throttle responses.
/// </summary>
internal sealed class FixedStatusHandler(HttpStatusCode statusCode, string body = "<html>error</html>")
    : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body),
        };
        return Task.FromResult(response);
    }
}
