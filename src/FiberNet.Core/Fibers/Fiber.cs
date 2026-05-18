using System.Runtime.CompilerServices;
using System.Threading;

namespace FiberNet.Core;

/// <summary>
/// Represents a cooperative fiber — a lightweight, userspace-scheduled unit of execution.
/// Fibers are always run on the <see cref="FiberScheduler"/> that created them and never
/// migrate across threads unless explicitly parked and re-queued.
/// </summary>
public sealed class Fiber
{
    private int _state; // 0 = idle, 1 = scheduled, 2 = running, 3 = parked, 4 = completed

    internal FiberScheduler? Scheduler { get; set; }
    internal Action? Continuation { get; set; }

    public FiberId Id { get; } = FiberId.Next();
    public bool IsCompleted => Volatile.Read(ref _state) == 4;
    public bool IsParked => Volatile.Read(ref _state) == 3;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TrySchedule()
    {
        return Interlocked.CompareExchange(ref _state, 1, 0) == 0
            || Interlocked.CompareExchange(ref _state, 1, 3) == 3;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void MarkRunning() => Volatile.Write(ref _state, 2);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void MarkIdle() => Volatile.Write(ref _state, 0);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void MarkParked() => Volatile.Write(ref _state, 3);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void MarkCompleted() => Volatile.Write(ref _state, 4);
}
