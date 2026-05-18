using System;
using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace FiberNet.Core;

/// <summary>
/// Zero-alloc awaitable that resumes the awaiting <see cref="Fiber"/> via the owning
/// <see cref="FiberScheduler"/> rather than the thread-pool.
/// </summary>
public struct FiberAwaitable : INotifyCompletion, IEquatable<FiberAwaitable>
{
    private int _completed; // 0 = pending, 1 = completed (accessed via Volatile/Interlocked)
    private Action? _continuation;

    public bool IsCompleted => Volatile.Read(ref _completed) == 1;

    public FiberAwaitable GetAwaiter() => this;

    public void GetResult()
    {
        // No result value — analogous to Task's void path.
    }

    public void OnCompleted(Action continuation)
    {
        ArgumentNullException.ThrowIfNull(continuation);
        _continuation = continuation;

        // If already completed by the time we register, run inline.
        if (Volatile.Read(ref _completed) == 1)
        {
            continuation();
        }
    }

    /// <summary>Signals completion and re-schedules the continuation fiber.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Complete()
    {
        Volatile.Write(ref _completed, 1);
        Interlocked.Exchange(ref _continuation, null)?.Invoke();
    }

    public bool Equals(FiberAwaitable other) =>
        _completed == other._completed && ReferenceEquals(_continuation, other._continuation);

    public override bool Equals(object? obj) => obj is FiberAwaitable other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(_completed, _continuation);

    public static bool operator ==(FiberAwaitable left, FiberAwaitable right) => left.Equals(right);
    public static bool operator !=(FiberAwaitable left, FiberAwaitable right) => !left.Equals(right);
}
