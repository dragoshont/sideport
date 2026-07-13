using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Netimobiledevice.Exceptions;
using Netimobiledevice.InstallationProxy;
using Netimobiledevice.Lockdown;
using Netimobiledevice.NotificationProxy;
using Netimobiledevice.Plist;
using Netimobiledevice.Usbmuxd;

namespace Sideport.Devices.Tests;

public sealed class VendoredFirstPairingTests
{
    private const string TestUdid = "00008110-0011223344556677";

    [Theory]
    [InlineData(false, "com.apple.mobile.notification_proxy", true)]
    [InlineData(true, "com.apple.mobile.insecure_notification_proxy", false)]
    public void NotificationProxy_UsesTheRequestedLockdownServiceAndTrustMode(
        bool useInsecureService,
        string expectedServiceName,
        bool expectedTrustedConnection)
    {
        using var lockdownTransport = LoopbackConnection.ForLockdown();
        using var notificationTransport = new LoopbackConnection();
        using var lockdown = new TestLockdownClient(
            lockdownTransport.Connection,
            startedService: notificationTransport.Connection);

        using var notificationProxy = new NotificationProxyService(lockdown, useInsecureService);

        Assert.Equal(expectedServiceName, lockdown.StartedServiceName);
        Assert.Equal(expectedTrustedConnection, lockdown.StartedWithTrustedConnection);
    }

    [Theory]
    [InlineData(true, false, "com.apple.mobile.notification_proxy")]
    [InlineData(true, true, "com.apple.mobile.insecure_notification_proxy")]
    [InlineData(false, false, "com.apple.mobile.notification_proxy.shim.remote")]
    [InlineData(false, true, "com.apple.mobile.insecure_notification_proxy.shim.remote")]
    public void NotificationProxy_ServiceNameMatchesProviderAndSecurityMode(
        bool isLockdownClient,
        bool useInsecureService,
        string expectedServiceName)
    {
        Assert.Equal(
            expectedServiceName,
            NotificationProxyService.GetServiceName(isLockdownClient, useInsecureService));
    }

    [Fact]
    public void PairDevice_AfterSuccessfulPairingValidation_DoesNotThrow()
    {
        using var transport = LoopbackConnection.ForLockdown();
        using var lockdown = new TestLockdownClient(
            transport.Connection,
            validationResults: [false, true]);

        Exception? exception = Record.Exception(() => lockdown.PairDevice());

        Assert.Null(exception);
        Assert.Equal(1, lockdown.PairCalls);
    }

    [Fact]
    public void PairDevice_WhenPairingStillDoesNotValidate_Throws()
    {
        using var transport = LoopbackConnection.ForLockdown();
        using var lockdown = new TestLockdownClient(
            transport.Connection,
            validationResults: [false, false]);

        Assert.Throws<FatalPairingException>(() => lockdown.PairDevice());
        Assert.Equal(1, lockdown.PairCalls);
    }

    [Fact]
    public void ConnectionMedium_IsTcpWithoutMuxMetadata_AndUsbmuxWithIt()
    {
        var muxDevice = new UsbmuxdDevice(42, TestUdid, UsbmuxdConnectionType.Usb);

        Assert.Equal(ConnectionMedium.TCP, LockdownClient.GetConnectionMedium(null));
        Assert.Equal(ConnectionMedium.USBMUX, LockdownClient.GetConnectionMedium(muxDevice));
    }

    [Fact]
    public void SavePairRecord_WritesTheLocalCacheAndPersistsThroughUsbmux()
    {
        string cachePath = Path.Combine(Path.GetTempPath(), $"sideport-pairing-{Guid.NewGuid():N}");
        var cacheDirectory = new DirectoryInfo(cachePath);
        var muxDevice = new UsbmuxdDevice(42, TestUdid, UsbmuxdConnectionType.Usb);

        try
        {
            using var transport = LoopbackConnection.ForLockdown(muxDevice);
            using var lockdown = new TestLockdownClient(
                transport.Connection,
                pairingRecordsCacheDirectory: cacheDirectory);
            lockdown.SetPairRecord(new DictionaryNode
            {
                { "HostID", new StringNode("host-id") },
                { "SystemBUID", new StringNode("system-buid") },
                { "EscrowBag", new DataNode([1, 2, 3, 4]) },
            });

            lockdown.SavePairRecord();

            string pairRecordPath = Path.Combine(cachePath, $"{TestUdid}.plist");
            Assert.True(File.Exists(pairRecordPath));
            Assert.NotNull(lockdown.SavedUsbmuxPairRecord);
            Assert.Equal(File.ReadAllBytes(pairRecordPath), lockdown.SavedUsbmuxPairRecord);
        }
        finally
        {
            if (Directory.Exists(cachePath))
            {
                Directory.Delete(cachePath, recursive: true);
            }
        }
    }

    [Fact]
    public void UsbmuxClient_PreservesTheConfiguredDaemonAddress()
    {
        const string customAddress = "/tmp/sideport-usbmuxd.sock";
        var muxDevice = new UsbmuxdDevice(42, TestUdid, UsbmuxdConnectionType.Usb);
        using var transport = LoopbackConnection.ForLockdown(muxDevice);
        using var lockdown = new InspectableUsbmuxLockdownClient(
            transport.Connection,
            customAddress);

        Assert.Equal(customAddress, lockdown.ConfiguredUsbmuxAddress);
    }

    [Fact]
    public async Task InstallationProxy_CancellationClosesAStalledAfcUpload()
    {
        string ipaPath = Path.Combine(Path.GetTempPath(), $"sideport-stalled-{Guid.NewGuid():N}.ipa");
        await File.WriteAllBytesAsync(ipaPath, new byte[128 * 1024]);

        try
        {
            using var lockdownTransport = LoopbackConnection.ForLockdown();
            using var installationProxyTransport = new LoopbackConnection();
            using var afcTransport = new LoopbackConnection();
            using var lockdown = new QueuedServiceLockdownClient(
                lockdownTransport.Connection,
                installationProxyTransport.Connection,
                afcTransport.Connection);
            using var installationProxy = new InstallationProxyService(lockdown);
            using var cancellation = new CancellationTokenSource();

            Task install = installationProxy.Install(ipaPath, cancellation.Token);
            await afcTransport.WaitForClientDataAsync(TimeSpan.FromSeconds(1));
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<Exception>(async () =>
                await install.WaitAsync(TimeSpan.FromSeconds(1)));
            Assert.True(install.IsCompleted);
        }
        finally
        {
            File.Delete(ipaPath);
        }
    }

    private static DictionaryNode DeviceValues() => new()
    {
        { "UniqueDeviceID", new StringNode(TestUdid) },
        { "ProductType", new StringNode("iPhone16,1") },
        { "ProductVersion", new StringNode("18.5") },
    };

    private sealed class TestLockdownClient : LockdownClient
    {
        private readonly Queue<bool> _validationResults;
        private readonly ServiceConnection? _startedService;

        public TestLockdownClient(
            ServiceConnection service,
            DirectoryInfo? pairingRecordsCacheDirectory = null,
            ServiceConnection? startedService = null,
            IEnumerable<bool>? validationResults = null)
            : base(
                service,
                hostId: "host-id",
                identifier: TestUdid,
                pairingRecordsCacheDirectory: pairingRecordsCacheDirectory)
        {
            _startedService = startedService;
            _validationResults = new Queue<bool>(validationResults ?? []);
        }

        public int PairCalls { get; private set; }

        public byte[]? SavedUsbmuxPairRecord { get; private set; }

        public string? StartedServiceName { get; private set; }

        public bool? StartedWithTrustedConnection { get; private set; }

        public override PropertyNode? GetValue(string? domain, string? key)
        {
            if (key == "ProductVersion") return new StringNode("18.5");
            if (key == "ProductType") return new StringNode("iPhone16,1");
            if (key == "UniqueDeviceID") return new StringNode(TestUdid);
            return DeviceValues();
        }

        public override ServiceConnection StartLockdownService(
            string name,
            bool useEscrowBag = false,
            bool useTrustedConnection = true)
        {
            StartedServiceName = name;
            StartedWithTrustedConnection = useTrustedConnection;
            return _startedService ?? throw new InvalidOperationException("No service connection was configured for this test.");
        }

        public void SetPairRecord(DictionaryNode pairRecord)
        {
            _pairRecord = pairRecord;
        }

        protected override LockdownError Pair()
        {
            PairCalls++;
            return LockdownError.Success;
        }

        protected override bool ValidatePairing()
        {
            return _validationResults.Count > 0 && _validationResults.Dequeue();
        }

        protected override void SavePairRecordToUsbmux(byte[] recordData)
        {
            SavedUsbmuxPairRecord = recordData;
        }
    }

    private sealed class InspectableUsbmuxLockdownClient : UsbmuxLockdownClient
    {
        public InspectableUsbmuxLockdownClient(ServiceConnection service, string usbmuxAddress)
            : base(
                service,
                hostId: "host-id",
                identifier: TestUdid,
                usbmuxAddress: usbmuxAddress)
        {
        }

        public string ConfiguredUsbmuxAddress => UsbmuxAddress;

        public override PropertyNode? GetValue(string? domain, string? key)
        {
            return DeviceValues();
        }
    }

    private sealed class QueuedServiceLockdownClient : LockdownClient
    {
        private readonly Queue<ServiceConnection> _services;

        public QueuedServiceLockdownClient(
            ServiceConnection lockdownService,
            params ServiceConnection[] services)
            : base(
                lockdownService,
                hostId: "host-id",
                identifier: TestUdid)
        {
            _services = new Queue<ServiceConnection>(services);
        }

        public override PropertyNode? GetValue(string? domain, string? key) => DeviceValues();

        public override ServiceConnection StartLockdownService(
            string name,
            bool useEscrowBag = false,
            bool useTrustedConnection = true) =>
            _services.Count > 0
                ? _services.Dequeue()
                : throw new InvalidOperationException($"No test transport remains for {name}.");
    }

    private sealed class LoopbackConnection : IDisposable
    {
        private static readonly ConstructorInfo ServiceConnectionConstructor =
            typeof(ServiceConnection).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                types: [typeof(Socket), typeof(ILogger), typeof(UsbmuxdDevice)],
                modifiers: null)
            ?? throw new InvalidOperationException("ServiceConnection socket constructor was not found.");

        private readonly Socket _serverSocket;

        public LoopbackConnection(UsbmuxdDevice? muxDevice = null)
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var endpoint = (IPEndPoint)listener.LocalEndpoint;

            var clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            clientSocket.Connect(endpoint);
            _serverSocket = listener.AcceptSocket();
            Connection = (ServiceConnection)ServiceConnectionConstructor.Invoke(
                [clientSocket, NullLogger.Instance, muxDevice]);
        }

        public ServiceConnection Connection { get; }

        public async Task WaitForClientDataAsync(TimeSpan timeout)
        {
            byte[] probe = new byte[1];
            int read = await _serverSocket.ReceiveAsync(probe, SocketFlags.Peek)
                .WaitAsync(timeout);
            Assert.Equal(1, read);
        }

        public static LoopbackConnection ForLockdown(UsbmuxdDevice? muxDevice = null)
        {
            var connection = new LoopbackConnection(muxDevice);
            connection.SendPlist(new DictionaryNode
            {
                { "Request", new StringNode("QueryType") },
                { "Type", new StringNode("com.apple.mobile.lockdown") },
            });
            return connection;
        }

        public void Dispose()
        {
            Connection.Dispose();
            _serverSocket.Dispose();
        }

        private void SendPlist(DictionaryNode response)
        {
            byte[] payload = PropertyList.SaveAsByteArray(response, PlistFormat.Xml);
            byte[] prefix = new byte[sizeof(int)];
            BinaryPrimitives.WriteInt32BigEndian(prefix, payload.Length);
            using var stream = new NetworkStream(_serverSocket, ownsSocket: false);
            stream.Write(prefix);
            stream.Write(payload);
            stream.Flush();
        }
    }
}
