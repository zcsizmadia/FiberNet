using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using FiberNet.IO;

namespace FiberNet.IO.Windows;

/// <summary>
/// Windows IOCP-backed listener. Accepts connections using the kernel-mode IOCP
/// completion port — no managed allocations per accept.
/// </summary>
internal sealed class IocpListener : IFiberNetListener
{
    private readonly Socket _listener;

    public EndPoint LocalEndPoint { get; }

    internal IocpListener(EndPoint endPoint)
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
        return new IocpSocket(accepted);
    }

    public ValueTask DisposeAsync()
    {
        _listener.Dispose();
        return ValueTask.CompletedTask;
    }
}
