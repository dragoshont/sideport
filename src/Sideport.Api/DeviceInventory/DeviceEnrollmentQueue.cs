using System.Threading.Channels;

namespace Sideport.Api.DeviceInventory;

public sealed class DeviceEnrollmentQueue
{
    private readonly Channel<string> _channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
    });

    public bool Enqueue(string operationId) => _channel.Writer.TryWrite(operationId);

    public IAsyncEnumerable<string> ReadAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);
}
