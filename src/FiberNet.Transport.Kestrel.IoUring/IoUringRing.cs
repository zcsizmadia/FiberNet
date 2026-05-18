using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using FiberNet.Transport.Kestrel.IoUring.Interop;

namespace FiberNet.Transport.Kestrel.IoUring;

/// <summary>
/// Managed wrapper around a native <c>fibernet_ring_t</c>.
/// Owns a dedicated completion thread that calls <c>fibernet_ring_wait_cqe</c>
/// in a tight loop and dispatches results via <see cref="IoUringPendingOps"/>.
/// </summary>
internal sealed class IoUringRing : IDisposable
{
    /// <summary>
    /// Sentinel <c>user_data</c> value for the NOP SQE that unblocks the
    /// completion thread on shutdown.
    /// </summary>
    private const ulong StopSentinel = ulong.MaxValue;

    private const uint DefaultQueueDepth = 256;

    private readonly nint _handle;
    private readonly IoUringPendingOps _ops;
    private readonly Thread _completionThread;

    /// <summary>
    /// Serialises all SQE submissions.  The completion thread only reads the CQ
    /// so it does not need this lock.
    /// </summary>
    private readonly object _sqLock = new();

    private int _disposed;

    /// <summary>Exposes the pending-ops registry to callers that need to register continuations.</summary>
    public IoUringPendingOps Ops => _ops;

    public IoUringRing(IoUringPendingOps ops, uint queueDepth = DefaultQueueDepth)
    {
        _ops = ops;
        _handle = IoUringInterop.RingCreate(queueDepth);
        if (_handle == 0)
        {
            throw new InvalidOperationException(
                "Failed to create io_uring ring. Linux kernel >= 5.1 is required.");
        }

        _completionThread = new Thread(CompletionLoop)
        {
            IsBackground = true,
            Name = "fibernet-uring-cq",
            Priority = ThreadPriority.AboveNormal,
        };
        _completionThread.Start();
    }

    // ── SQE submission ────────────────────────────────────────────────────────

    public void SubmitAccept(int serverFd, ulong userData)
    {
        lock (_sqLock)
        {
            CheckNativeError(IoUringInterop.RingSubmitAccept(_handle, serverFd, userData), "accept");
            CheckNativeError(IoUringInterop.RingSubmit(_handle), "submit");
        }
    }

    public void SubmitRecv(int fd, nint buf, uint len, ulong userData)
    {
        lock (_sqLock)
        {
            CheckNativeError(IoUringInterop.RingSubmitRecv(_handle, fd, buf, len, userData), "recv");
            CheckNativeError(IoUringInterop.RingSubmit(_handle), "submit");
        }
    }

    public void SubmitSend(int fd, nint buf, uint len, ulong userData)
    {
        lock (_sqLock)
        {
            CheckNativeError(IoUringInterop.RingSubmitSend(_handle, fd, buf, len, userData), "send");
            CheckNativeError(IoUringInterop.RingSubmit(_handle), "submit");
        }
    }

    /// <summary>
    /// Posts an async close via the ring.  The completion is not tracked
    /// (<c>user_data = 0</c>) since callers do not need to await it.
    /// </summary>
    public void SubmitClose(int fd)
    {
        lock (_sqLock)
        {
            // Best-effort async close; ignore return values intentionally.
            _ = IoUringInterop.RingSubmitClose(_handle, fd, 0);
            _ = IoUringInterop.RingSubmit(_handle);
        }
    }

    // ── Completion thread ─────────────────────────────────────────────────────

    private void CompletionLoop()
    {
        while (true)
        {
            int ret = IoUringInterop.RingWaitCqe(_handle, out var userData, out var res, out _);

            if (ret < 0)
            {
                // -EINTR (signal interrupted): safe to retry.
                continue;
            }

            if (userData == StopSentinel)
            {
                break;
            }

            // user_data == 0 → untracked op (e.g. SubmitClose); skip.
            if (userData != 0)
            {
                _ops.Complete(userData, res);
            }
        }
    }

    // ── Finalizer (GC path — no thread join; background thread exits via IsBackground) ─

    ~IoUringRing()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        {
            return;
        }

        IoUringInterop.RingDestroy(_handle);
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        {
            return;
        }

        // Unblock the completion thread with a NOP sentinel.
        lock (_sqLock)
        {
            _ = IoUringInterop.RingSubmitNop(_handle, StopSentinel);
            _ = IoUringInterop.RingSubmit(_handle);
        }

        _completionThread.Join(millisecondsTimeout: 2_000);
        _ops.Dispose();
        IoUringInterop.RingDestroy(_handle);
        GC.SuppressFinalize(this);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void CheckNativeError(int ret, string op)
    {
        if (ret < 0)
        {
            throw new IOException($"io_uring {op} failed: errno={-ret}");
        }
    }
}
