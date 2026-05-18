using System;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections;

namespace FiberNet.Transport.Kestrel;

/// <summary>
/// Kestrel <see cref="IConnectionListener"/> backed by a raw <see cref="Socket"/> accept loop.
/// Accepted connections are wrapped in <see cref="FiberNetConnectionContext"/> which provides
/// a zero-copy <see cref="IDuplexPipe"/>.
/// </summary>
internal sealed class FiberNetConnectionListener : IConnectionListener
{
    private readonly Socket _listener;

    public EndPoint EndPoint { get; }

    internal FiberNetConnectionListener(EndPoint endPoint)
    {
        EndPoint = endPoint;
        _listener = new Socket(endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = true,
        };
    }

    internal void Bind()
    {
        _listener.Bind(EndPoint);
        _listener.Listen(128);
    }

    public async ValueTask<ConnectionContext?> AcceptAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var socket = await _listener.AcceptAsync(cancellationToken).ConfigureAwait(false);
            socket.NoDelay = true;
            return new FiberNetConnectionContext(socket);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    public ValueTask UnbindAsync(CancellationToken cancellationToken = default)
    {
        _listener.Dispose();
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _listener.Dispose();
        return ValueTask.CompletedTask;
    }
}
