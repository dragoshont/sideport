using Claunia.PropertyList;

namespace Sideport.DeveloperApi.Tests.Support;

internal sealed class TamperAppTokenChecksumHandler : DelegatingHandler
{
    public TamperAppTokenChecksumHandler(HttpMessageHandler innerHandler)
        : base(innerHandler)
    {
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        byte[] bytes = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
        var root = (NSDictionary)PropertyListParser.Parse(bytes);
        var parameters = (NSDictionary)root["Request"];
        if (parameters["o"].ToString() == "apptokens")
        {
            byte[] checksum = ((NSData)parameters["checksum"]).Bytes.ToArray();
            checksum[0] ^= 0x80;
            parameters["checksum"] = new NSData(checksum);

            byte[] tampered =
                System.Text.Encoding.UTF8.GetBytes(root.ToXmlPropertyList());
            request.Content = new ByteArrayContent(tampered);
            request.Content.Headers.ContentType =
                new System.Net.Http.Headers.MediaTypeHeaderValue("text/x-xml-plist");
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
