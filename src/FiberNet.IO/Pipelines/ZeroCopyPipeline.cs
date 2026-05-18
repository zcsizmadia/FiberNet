using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;

namespace FiberNet.IO;

/// <summary>
/// A zero-copy duplex pipeline that wraps a <see cref="IDuplexPipe"/> and exposes
/// <see cref="ReadOnlySequence{T}"/> for reads — no intermediate copies.
/// </summary>
public sealed class ZeroCopyPipeline : IAsyncDisposable
{
    private readonly IDuplexPipe _transport;

    public ZeroCopyPipeline(IDuplexPipe transport) => _transport = transport;

    public PipeReader Input => _transport.Input;
    public PipeWriter Output => _transport.Output;

    /// <summary>
    /// Advances the reader past <paramref name="examined"/> bytes — zero-copy,
    /// no buffer duplication.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Advance(SequencePosition consumed, SequencePosition examined) =>
        _transport.Input.AdvanceTo(consumed, examined);

    public async ValueTask DisposeAsync()
    {
        await _transport.Input.CompleteAsync().ConfigureAwait(false);
        await _transport.Output.CompleteAsync().ConfigureAwait(false);
    }
}
