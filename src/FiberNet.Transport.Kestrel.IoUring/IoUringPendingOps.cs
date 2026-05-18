using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace FiberNet.Transport.Kestrel.IoUring;

/// <summary>
/// Thread-safe registry that maps io_uring <c>user_data</c> tokens to the
/// <see cref="TaskCompletionSource{T}"/> awaiting each in-flight operation.
/// </summary>
internal sealed class IoUringPendingOps : IDisposable
{
    private readonly ConcurrentDictionary<ulong, TaskCompletionSource<int>> _pending = new();
    private ulong _nextToken;
    private bool _disposed;

    /// <summary>
    /// Registers <paramref name="tcs"/> and returns the unique token to embed
    /// as <c>user_data</c> in the SQE.
    /// </summary>
    public ulong Register(TaskCompletionSource<int> tcs)
    {
        var token = Interlocked.Increment(ref _nextToken);
        _pending[token] = tcs;
        return token;
    }

    /// <summary>
    /// Resolves the continuation registered for <paramref name="token"/>.
    /// Negative <paramref name="result"/> is treated as a negated errno and
    /// converted to a <see cref="SocketException"/>.
    /// </summary>
    public void Complete(ulong token, int result)
    {
        if (!_pending.TryRemove(token, out var tcs))
        {
            return;
        }

        if (result < 0)
        {
            tcs.TrySetException(new SocketException(-result));
        }
        else
        {
            tcs.TrySetResult(result);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var kv in _pending)
        {
            kv.Value.TrySetCanceled();
        }

        _pending.Clear();
        GC.SuppressFinalize(this);
    }
}
