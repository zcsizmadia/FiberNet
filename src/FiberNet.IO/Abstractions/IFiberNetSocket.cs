using System;
using System.Buffers;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace FiberNet.IO;

/// <summary>
/// A fiber-aware, zero-copy socket abstraction.
/// All I/O operations return <see cref="ValueTask{T}"/> to avoid allocations on the hot path.
/// </summary>
public interface IFiberNetSocket : IAsyncDisposable
{
    EndPoint? LocalEndPoint { get; }
    EndPoint? RemoteEndPoint { get; }

    ValueTask ConnectAsync(EndPoint endPoint, CancellationToken cancellationToken = default);
    ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken = default);
    ValueTask<int> SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default);
    ValueTask<int> SendAsync(ReadOnlySequence<byte> sequence, CancellationToken cancellationToken = default);
    void Shutdown();
}
