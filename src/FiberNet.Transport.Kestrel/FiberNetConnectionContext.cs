using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http.Features;

namespace FiberNet.Transport.Kestrel;

/// <summary>
/// Kestrel <see cref="ConnectionContext"/> that wraps an accepted <see cref="Socket"/> and
/// exposes a zero-copy <see cref="IDuplexPipe"/> via <see cref="System.IO.Pipelines"/>.
/// </summary>
internal sealed class FiberNetConnectionContext : ConnectionContext
{
    private readonly Socket _socket;
    private readonly Pipe _inputPipe = new();
    private readonly Pipe _outputPipe = new();
    private readonly CancellationTokenSource _cts = new();

    public override string ConnectionId { get; set; } = Guid.NewGuid().ToString("N");
    public override IDuplexPipe Transport { get; set; }
    public override IFeatureCollection Features { get; } = new FeatureCollection();
    public override IDictionary<object, object?> Items { get; set; } = new Dictionary<object, object?>();
    public override EndPoint? LocalEndPoint => _socket.LocalEndPoint;
    public override EndPoint? RemoteEndPoint => _socket.RemoteEndPoint;

    internal FiberNetConnectionContext(Socket socket)
    {
        _socket = socket;
        Transport = new DuplexPipe(_inputPipe.Reader, _outputPipe.Writer);
        _ = RunReceiveLoopAsync();
        _ = RunSendLoopAsync();
    }

    private async Task RunReceiveLoopAsync()
    {
        var writer = _inputPipe.Writer;

        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var memory = writer.GetMemory(4096);
                var bytesRead = await _socket
                    .ReceiveAsync(memory, SocketFlags.None, _cts.Token)
                    .ConfigureAwait(false);

                if (bytesRead == 0)
                {
                    break;
                }

                writer.Advance(bytesRead);
                var result = await writer.FlushAsync(_cts.Token).ConfigureAwait(false);

                if (result.IsCompleted || result.IsCanceled)
                {
                    break;
                }
            }
        }
        finally
        {
            await writer.CompleteAsync().ConfigureAwait(false);
        }
    }

    private async Task RunSendLoopAsync()
    {
        var reader = _outputPipe.Reader;

        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var result = await reader.ReadAsync(_cts.Token).ConfigureAwait(false);
                var buffer = result.Buffer;

                foreach (var segment in buffer)
                {
                    await _socket.SendAsync(segment, SocketFlags.None, _cts.Token).ConfigureAwait(false);
                }

                reader.AdvanceTo(buffer.End);

                if (result.IsCompleted || result.IsCanceled)
                {
                    break;
                }
            }
        }
        finally
        {
            await reader.CompleteAsync().ConfigureAwait(false);
        }
    }

    public override async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        _socket.Dispose();
        _cts.Dispose();
        await base.DisposeAsync().ConfigureAwait(false);
    }

    private sealed class DuplexPipe(PipeReader reader, PipeWriter writer) : IDuplexPipe
    {
        public PipeReader Input => reader;
        public PipeWriter Output => writer;
    }
}
