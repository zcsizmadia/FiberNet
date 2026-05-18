using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading;

namespace FiberNet.Core;

/// <summary>
/// A fiber-aware, zero-alloc multi-producer single-consumer channel.
/// Writers never block; the single reader fiber is woken via <see cref="FiberChannelAwaitable{T}"/>.
/// </summary>
/// <typeparam name="T">The message type. Should be a value type for zero-alloc paths.</typeparam>
public sealed class FiberChannel<T>
{
    private readonly ConcurrentQueue<T> _queue = new();
    private Action? _waitCallback;

    /// <summary>Enqueues an item and wakes the waiting fiber if parked.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(T item)
    {
        _queue.Enqueue(item);
        Interlocked.Exchange(ref _waitCallback, null)?.Invoke();
    }

    /// <summary>Attempts to dequeue without blocking.</summary>
    public bool TryRead(out T item) => _queue.TryDequeue(out item!);

    internal bool HasItems => !_queue.IsEmpty;

    internal void SetCallback(Action? callback) => Volatile.Write(ref _waitCallback, callback);

    /// <summary>Returns an awaitable that completes when the next item is available.</summary>
    public FiberChannelAwaitable<T> WaitToReadAsync() => new(this);
}

/// <summary>
/// Zero-alloc awaitable returned by <see cref="FiberChannel{T}.WaitToReadAsync"/>.
/// Holds a reference to the channel so <see cref="IsCompleted"/> always reflects live state.
/// </summary>
public readonly struct FiberChannelAwaitable<T> : INotifyCompletion, IEquatable<FiberChannelAwaitable<T>>
{
    private readonly FiberChannel<T> _channel;

    internal FiberChannelAwaitable(FiberChannel<T> channel) => _channel = channel;

    public bool IsCompleted => _channel.HasItems;

    public FiberChannelAwaitable<T> GetAwaiter() => this;

    public void GetResult() { }

    public void OnCompleted(Action continuation)
    {
        ArgumentNullException.ThrowIfNull(continuation);
        _channel.SetCallback(continuation);

        // Guard against the item arriving between IsCompleted check and SetCallback.
        if (_channel.HasItems)
        {
            _channel.SetCallback(null);
            continuation();
        }
    }

    public bool Equals(FiberChannelAwaitable<T> other) => ReferenceEquals(_channel, other._channel);
    public override bool Equals(object? obj) => obj is FiberChannelAwaitable<T> other && Equals(other);
    public override int GetHashCode() => _channel?.GetHashCode() ?? 0;

    public static bool operator ==(FiberChannelAwaitable<T> left, FiberChannelAwaitable<T> right) => left.Equals(right);
    public static bool operator !=(FiberChannelAwaitable<T> left, FiberChannelAwaitable<T> right) => !left.Equals(right);
}
