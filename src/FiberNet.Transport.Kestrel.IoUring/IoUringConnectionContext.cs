using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http.Features;

namespace FiberNet.Transport.Kestrel.IoUring;

/// <summary>
/// Kestrel <see cref="ConnectionContext"/> backed by an io_uring-dispatched
/// socket file descriptor.  Receive and send loops use pre-pinned heap buffers
/// so that the GC never moves the memory referenced by in-flight SQEs.
/// </summary>
internal sealed class IoUringConnectionContext : ConnectionContext
{
    private const int RecvBufSize = 65_536;
    private const int SendBufSize = 65_536;

    private readonly int _fd;
    private readonly IoUringRing _ring;
    private readonly Pipe _inputPipe = new();
    private readonly Pipe _outputPipe = new();
    private readonly CancellationTokenSource _cts = new();

    // GC.AllocateArray with pinned:true produces a heap array that the GC will
    // never relocate, giving us a stable native pointer for the lifetime of
    // this connection.
    private readonly byte[] _recvBuf = GC.AllocateArray<byte>(RecvBufSize, pinned: true);
    private readonly byte[] _sendBuf = GC.AllocateArray<byte>(SendBufSize, pinned: true);

    public override string ConnectionId { get; set; } = Guid.NewGuid().ToString("N");
    public override IDuplexPipe Transport { get; set; }
    public override IFeatureCollection Features { get; } = new FeatureCollection();
    public override IDictionary<object, object?> Items { get; set; } = new Dictionary<object, object?>();

    // Raw fd — no .NET Socket wrapper, so endpoints are not available here.
    public override EndPoint? LocalEndPoint  => null;
    public override EndPoint? RemoteEndPoint => null;

    internal IoUringConnectionContext(int fd, IoUringRing ring)
    {
        _fd   = fd;
        _ring = ring;
        Transport = new DuplexPipe(_inputPipe.Reader, _outputPipe.Writer);
        _ = RunReceiveLoopAsync();
        _ = RunSendLoopAsync();
    }

    // ── Receive loop ──────────────────────────────────────────────────────────

    private async Task RunReceiveLoopAsync()
    {
        var writer  = _inputPipe.Writer;
        var recvPtr = GetPinnedPtr(_recvBuf);

        try
        {
            await ReceiveLoopCoreAsync(writer, recvPtr).ConfigureAwait(false);
        }
        finally
        {
            await writer.CompleteAsync().ConfigureAwait(false);
        }
    }

    private async Task ReceiveLoopCoreAsync(PipeWriter writer, nint recvPtr)
    {
        while (!_cts.IsCancellationRequested)
        {
            var tcs = new TaskCompletionSource<int>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var token = _ring.Ops.Register(tcs);
            _ring.SubmitRecv(_fd, recvPtr, RecvBufSize, token);

            int bytesRead;
            try
            {
                bytesRead = await tcs.Task.ConfigureAwait(false);
            }
            catch (SocketException)
            {
                return;
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (bytesRead <= 0)
            {
                return;
            }

            // Copy from the pinned native buffer into the pipe writer's segment.
            var dest = writer.GetMemory(bytesRead);
            _recvBuf.AsSpan(0, bytesRead).CopyTo(dest.Span);
            writer.Advance(bytesRead);

            FlushResult flush;
            try
            {
                flush = await writer.FlushAsync(_cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (flush.IsCompleted || flush.IsCanceled)
            {
                return;
            }
        }
    }

    // ── Send loop ─────────────────────────────────────────────────────────────

    private async Task RunSendLoopAsync()
    {
        var reader  = _outputPipe.Reader;
        var sendPtr = GetPinnedPtr(_sendBuf);

        try
        {
            await SendLoopCoreAsync(reader, sendPtr).ConfigureAwait(false);
        }
        finally
        {
            await reader.CompleteAsync().ConfigureAwait(false);
        }
    }

    private async Task SendLoopCoreAsync(PipeReader reader, nint sendPtr)
    {
        while (!_cts.IsCancellationRequested)
        {
            ReadResult result;
            try
            {
                result = await reader.ReadAsync(_cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            var buffer = result.Buffer;

            foreach (var segment in buffer)
            {
                var remaining = segment;
                while (!remaining.IsEmpty)
                {
                    var chunkLen = (int)Math.Min(remaining.Length, SendBufSize);

                    // Copy from pipe segment into the pinned send buffer.
                    remaining.Span[..chunkLen].CopyTo(_sendBuf.AsSpan(0, chunkLen));

                    var tcs = new TaskCompletionSource<int>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    var token = _ring.Ops.Register(tcs);
                    _ring.SubmitSend(_fd, sendPtr, (uint)chunkLen, token);

                    try
                    {
                        await tcs.Task.ConfigureAwait(false);
                    }
                    catch (SocketException)
                    {
                        reader.AdvanceTo(buffer.End);
                        return;
                    }
                    catch (OperationCanceledException)
                    {
                        reader.AdvanceTo(buffer.End);
                        return;
                    }

                    remaining = remaining.Slice(chunkLen);
                }
            }

            reader.AdvanceTo(buffer.End);

            if (result.IsCompleted || result.IsCanceled)
            {
                return;
            }
        }
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public override async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        _ring.SubmitClose(_fd);
        _cts.Dispose();
        await base.DisposeAsync().ConfigureAwait(false);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the address of the first element of a pinned array
    /// (allocated via <see cref="GC.AllocateArray{T}"/> with <c>pinned: true</c>).
    /// The returned pointer is stable for the lifetime of the array.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe nint GetPinnedPtr(byte[] arr)
    {
        fixed (byte* p = arr)
        {
            return (nint)p;
        }
    }

    private sealed class DuplexPipe(PipeReader reader, PipeWriter writer) : IDuplexPipe
    {
        public PipeReader Input  => reader;
        public PipeWriter Output => writer;
    }
}
