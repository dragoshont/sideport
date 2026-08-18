using System.Net;
using System.Net.Sockets;
using Netimobiledevice.Exceptions;
using Netimobiledevice.Usbmuxd;

namespace Sideport.Devices.Tests;

public sealed class VendoredUsbmuxdSocketTests
{
    [Fact]
    public async Task Receive_AccumulatesPartialStreamReads()
    {
        using ConnectedSockets sockets = await ConnectedSockets.CreateAsync();
        var usbmux = new UsbmuxdSocket(sockets.Client);
        byte[] expected = Enumerable.Range(0, 64).Select(value => (byte)value).ToArray();

        Task writer = Task.Run(async () =>
        {
            await sockets.Server.SendAsync(expected.AsMemory(0, 7));
            await Task.Delay(10);
            await sockets.Server.SendAsync(expected.AsMemory(7, 19));
            await Task.Delay(10);
            await sockets.Server.SendAsync(expected.AsMemory(26));
        });

        byte[] received = usbmux.Receive(expected.Length);
        await writer;

        Assert.Equal(expected, received);
    }

    [Fact]
    public async Task Receive_ThrowsWhenPeerClosesMidFrame()
    {
        using ConnectedSockets sockets = await ConnectedSockets.CreateAsync();
        var usbmux = new UsbmuxdSocket(sockets.Client);
        await sockets.Server.SendAsync(new byte[] { 1, 2, 3 });
        sockets.Server.Shutdown(SocketShutdown.Send);

        UsbmuxException error = Assert.Throws<UsbmuxException>(() => usbmux.Receive(8));

        Assert.Contains("3 of 8", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Send_WritesTheCompleteBuffer()
    {
        using ConnectedSockets sockets = await ConnectedSockets.CreateAsync();
        sockets.Client.SendBufferSize = 1_024;
        var usbmux = new UsbmuxdSocket(sockets.Client);
        byte[] expected = Enumerable.Range(0, 256 * 1_024).Select(value => (byte)(value % 251)).ToArray();
        byte[] received = new byte[expected.Length];

        Task reader = Task.Run(async () =>
        {
            int offset = 0;
            while (offset < received.Length)
            {
                int count = await sockets.Server.ReceiveAsync(received.AsMemory(offset, Math.Min(2_047, received.Length - offset)));
                Assert.NotEqual(0, count);
                offset += count;
            }
        });

        int sent = usbmux.Send(expected);
        await reader;

        Assert.Equal(expected.Length, sent);
        Assert.Equal(expected, received);
    }

    private sealed class ConnectedSockets(Socket client, Socket server, TcpListener listener) : IDisposable
    {
        public Socket Client { get; } = client;
        public Socket Server { get; } = server;

        public static async Task<ConnectedSockets> CreateAsync()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            Task<Socket> accept = listener.AcceptSocketAsync();
            await client.ConnectAsync((IPEndPoint)listener.LocalEndpoint);
            Socket server = await accept;
            return new ConnectedSockets(client, server, listener);
        }

        public void Dispose()
        {
            Client.Dispose();
            Server.Dispose();
            listener.Stop();
        }
    }
}
