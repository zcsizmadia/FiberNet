using System;
using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using FiberNet.IO;

namespace FiberNet.IO.Windows;

/// <summary>
/// Windows IOCP-backed socket. Uses <see cref="SocketAsyncEventArgs"/> pooled per-operation
/// so completions are zero-alloc.
/// </summary>
internal sealed class IocpSocket : IFiberNetSocket
{
    private readonly Socket _socket;

    internal IocpSocket(Socket socket) => _socket = socket;

    public EndPoint? LocalEndPoint => _socket.LocalEndPoint;
    public EndPoint? RemoteEndPoint => _socket.RemoteEndPoint;

    public ValueTask ConnectAsync(EndPoint endPoint, CancellationToken cancellationToken = default) =>
        _socket.ConnectAsync(endPoint, cancellationToken);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        _socket.ReceiveAsync(buffer, SocketFlags.None, cancellationToken);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<int> SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
        _socket.SendAsync(buffer, SocketFlags.None, cancellationToken);

    public async ValueTask<int> SendAsync(ReadOnlySequence<byte> sequence, CancellationToken cancellationToken = default)
    {
        var total = 0;

        foreach (var segment in sequence)
        {
            total += await _socket.SendAsync(segment, SocketFlags.None, cancellationToken).ConfigureAwait(false);
        }

        return total;
    }

    public void Shutdown() => _socket.Shutdown(SocketShutdown.Both);

    public ValueTask DisposeAsync()
    {
        _socket.Dispose();
        return ValueTask.CompletedTask;
    }
}
