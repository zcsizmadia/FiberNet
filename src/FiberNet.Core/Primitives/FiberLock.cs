using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace FiberNet.Core;

/// <summary>
/// A fiber-aware, zero-alloc mutual exclusion lock implemented as a userspace futex.
/// Fibers that fail to acquire are parked (not blocked) and re-queued when the lock is released.
/// </summary>
public sealed class FiberLock
{
    private int _locked; // 0 = free, 1 = held (accessed via Interlocked/Volatile)
    private FiberScheduler? _ownerScheduler;
    private Fiber? _waitingFiber;

    /// <summary>
    /// Attempts to acquire the lock immediately.
    /// Returns <see langword="true"/> if the lock was taken; <see langword="false"/> otherwise.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryAcquire() => Interlocked.CompareExchange(ref _locked, 1, 0) == 0;

    /// <summary>Parks <paramref name="fiber"/> until the lock becomes free.</summary>
    public void ParkUntilFree(Fiber fiber, FiberScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(fiber);
        ArgumentNullException.ThrowIfNull(scheduler);
        fiber.MarkParked();
        Volatile.Write(ref _waitingFiber, fiber);
        Volatile.Write(ref _ownerScheduler, scheduler);
    }

    /// <summary>Releases the lock and re-queues any parked fiber.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Release()
    {
        Volatile.Write(ref _locked, 0);

        var waiting = Interlocked.Exchange(ref _waitingFiber, null);
        var scheduler = _ownerScheduler;

        if (waiting is not null && scheduler is not null)
        {
            scheduler.Enqueue(waiting);
        }
    }
}
