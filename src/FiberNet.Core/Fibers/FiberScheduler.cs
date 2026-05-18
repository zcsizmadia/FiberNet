using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace FiberNet.Core;

/// <summary>
/// Cooperative, work-stealing fiber scheduler backed by a lock-free <see cref="ConcurrentQueue{T}"/>.
/// Each scheduler owns exactly one OS thread. Fibers are enqueued and drained in FIFO order.
/// </summary>
public sealed partial class FiberScheduler : IDisposable
{
    private readonly ConcurrentQueue<Fiber> _readyQueue = new();
    private readonly Thread _thread;
    private readonly CancellationTokenSource _cts = new();
    private readonly ILogger<FiberScheduler> _logger;
    private bool _disposed;

    public FiberScheduler(ILogger<FiberScheduler> logger)
    {
        _logger = logger;
        _thread = new Thread(RunLoop)
        {
            IsBackground = true,
            Name = $"FiberScheduler-{Environment.CurrentManagedThreadId}",
        };
        _thread.Start();
    }

    /// <summary>Creates a new <see cref="Fiber"/> and enqueues it for execution.</summary>
    public Fiber Spawn(Action continuation)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var fiber = new Fiber
        {
            Scheduler = this,
            Continuation = continuation,
        };

        Enqueue(fiber);
        return fiber;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void Enqueue(Fiber fiber)
    {
        if (fiber.TrySchedule())
        {
            _readyQueue.Enqueue(fiber);
        }
    }

    private void RunLoop()
    {
        var token = _cts.Token;

        while (!token.IsCancellationRequested)
        {
            if (_readyQueue.TryDequeue(out var fiber))
            {
                Execute(fiber);
            }
            else
            {
                Thread.SpinWait(20);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Execute(Fiber fiber)
    {
        fiber.MarkRunning();

        try
        {
            fiber.Continuation?.Invoke();
        }
        catch (OperationCanceledException ex)
        {
            Log.FiberCanceled(_logger, fiber.Id, ex);
        }
        catch (InvalidOperationException ex)
        {
            Log.FiberFailed(_logger, fiber.Id, ex);
        }
        finally
        {
            fiber.MarkCompleted();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cts.Cancel();
        _cts.Dispose();
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning, Message = "Fiber {FiberId} was canceled")]
        internal static partial void FiberCanceled(ILogger logger, FiberId fiberId, OperationCanceledException ex);

        [LoggerMessage(Level = LogLevel.Error, Message = "Fiber {FiberId} faulted")]
        internal static partial void FiberFailed(ILogger logger, FiberId fiberId, InvalidOperationException ex);
    }
}
