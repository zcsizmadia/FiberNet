using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using FiberNet.IO;

namespace FiberNet.IO.Linux;

/// <summary>
/// Linux SAEA-backed listener.
/// </summary>
internal sealed class SaeaListener : IFiberNetListener
{
    private readonly Socket _listener;

    public EndPoint LocalEndPoint { get; }

    internal SaeaListener(EndPoint endPoint)
    {
        LocalEndPoint = endPoint;
        _listener = new Socket(endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = true,
        };
    }

    public void Bind() => _listener.Bind(LocalEndPoint);

    public void Listen(int backlog = 128) => _listener.Listen(backlog);

    public async ValueTask<IFiberNetSocket> AcceptAsync(CancellationToken cancellationToken = default)
    {
        var accepted = await _listener.AcceptAsync(cancellationToken).ConfigureAwait(false);
        accepted.NoDelay = true;
        return new SaeaSocket(accepted);
    }

    public ValueTask DisposeAsync()
    {
        _listener.Dispose();
        return ValueTask.CompletedTask;
    }
}
