using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Sideport.DeveloperApi.GrandSlam;

namespace Sideport.DeveloperApi.Tests.GrandSlam;

public sealed class GrandSlamTransportTests
{
    [Fact]
    public async Task ProductionHandler_UsesFreshTcpConnectionForEveryRequest()
    {
        await using var server = new ConnectionSensitiveServer();
        var services = new ServiceCollection();
        services.AddAppleDeveloperPortal(server.Address, "test-device");
        using ServiceProvider provider = services.BuildServiceProvider();
        using HttpClient http = provider.GetRequiredService<IHttpClientFactory>()
            .CreateClient(nameof(GrandSlamClient));

        for (int i = 0; i < 3; i++)
        {
            using HttpResponseMessage response = await http.GetAsync(server.Address);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.Equal(3, server.ConnectionCount);
    }

    [Fact]
    public async Task LegacyPoolingFixture_ReproducesThirdRequest503OnSharedConnection()
    {
        await using var server = new ConnectionSensitiveServer();
        using var http = new HttpClient(new SocketsHttpHandler());

        using HttpResponseMessage first = await http.GetAsync(server.Address);
        using HttpResponseMessage second = await http.GetAsync(server.Address);
        using HttpResponseMessage third = await http.GetAsync(server.Address);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, third.StatusCode);
        Assert.Equal(1, server.ConnectionCount);
    }

    private sealed class ConnectionSensitiveServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stopping = new();
        private readonly Task _acceptLoop;
        private int _connections;

        public ConnectionSensitiveServer()
        {
            _listener.Start();
            int port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            Address = new Uri($"http://127.0.0.1:{port}/");
            _acceptLoop = AcceptLoopAsync();
        }

        public Uri Address { get; }

        public int ConnectionCount => Volatile.Read(ref _connections);

        public async ValueTask DisposeAsync()
        {
            _stopping.Cancel();
            _listener.Stop();
            try
            {
                await _acceptLoop;
            }
            catch (OperationCanceledException)
            {
            }
            _stopping.Dispose();
        }

        private async Task AcceptLoopAsync()
        {
            while (!_stopping.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(_stopping.Token);
                }
                catch (SocketException) when (_stopping.IsCancellationRequested)
                {
                    return;
                }

                Interlocked.Increment(ref _connections);
                _ = ServeConnectionAsync(client, _stopping.Token);
            }
        }

        private static async Task ServeConnectionAsync(
            TcpClient client, CancellationToken cancellationToken)
        {
            using (client)
            {
                NetworkStream stream = client.GetStream();
                int requestNumber = 0;
                while (await ReadHeadersAsync(stream, cancellationToken))
                {
                    requestNumber++;
                    int status = requestNumber == 3 ? 503 : 200;
                    string reason = status == 200 ? "OK" : "Service Unavailable";
                    byte[] response = Encoding.ASCII.GetBytes(
                        $"HTTP/1.1 {status} {reason}\r\nContent-Length: 0\r\n" +
                        "Connection: keep-alive\r\n\r\n");
                    await stream.WriteAsync(response, cancellationToken);
                }
            }
        }

        private static async Task<bool> ReadHeadersAsync(
            NetworkStream stream, CancellationToken cancellationToken)
        {
            int matched = 0;
            byte[] terminator = "\r\n\r\n"u8.ToArray();
            var oneByte = new byte[1];
            while (true)
            {
                int read = await stream.ReadAsync(oneByte, cancellationToken);
                if (read == 0)
                    return false;
                matched = oneByte[0] == terminator[matched] ? matched + 1 : 0;
                if (matched == terminator.Length)
                    return true;
            }
        }
    }
}
