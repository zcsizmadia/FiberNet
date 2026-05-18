using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using FiberNet.Transport.Kestrel.IoUring.Interop;
using Microsoft.AspNetCore.Connections;

namespace FiberNet.Transport.Kestrel.IoUring;

/// <summary>
/// Kestrel <see cref="IConnectionListener"/> that accepts connections via io_uring.
/// The server socket is created by the native bridge and kept as a raw fd.
/// Each accepted connection is wrapped in an <see cref="IoUringConnectionContext"/>.
/// </summary>
internal sealed class IoUringConnectionListener : IConnectionListener
{
    private readonly int _serverFd;
    private readonly IoUringRing _ring;

    public EndPoint EndPoint { get; }

    internal IoUringConnectionListener(EndPoint endPoint, int serverFd, IoUringRing ring)
    {
        EndPoint  = endPoint;
        _serverFd = serverFd;
        _ring     = ring;
    }

    public async ValueTask<ConnectionContext?> AcceptAsync(
        CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var tcs = new TaskCompletionSource<int>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var token = _ring.Ops.Register(tcs);
            _ring.SubmitAccept(_serverFd, token);

            int clientFd;
            try
            {
                clientFd = await tcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (SocketException)
            {
                // Transient accept error; re-post and retry.
                continue;
            }

            if (clientFd < 0)
            {
                continue;
            }

            return new IoUringConnectionContext(clientFd, _ring);
        }

        return null;
    }

    public ValueTask UnbindAsync(CancellationToken cancellationToken = default)
    {
        _ = IoUringInterop.CloseFd(_serverFd);
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _ring.Dispose();
        return ValueTask.CompletedTask;
    }
}
