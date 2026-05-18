using System.IO.Pipelines;
using System.Text;
using FiberNet.IO;

// Demonstrate zero-copy reads from a System.IO.Pipelines pipe.
var pipe = new Pipe();
var pipeline = new ZeroCopyPipeline(new SimpleDuplexPipe(pipe.Reader, pipe.Writer));

// Writer side
var writer = pipeline.Output;
var memory = writer.GetMemory(256);
var bytesWritten = Encoding.UTF8.GetBytes("Zero-copy FiberNet pipeline demo", memory.Span);
writer.Advance(bytesWritten);
await writer.FlushAsync();

// Reader side — no buffer copy
var result = await pipeline.Input.ReadAsync();
var seq = result.Buffer;

Console.WriteLine(Encoding.UTF8.GetString(seq.FirstSpan));
pipeline.Advance(seq.End, seq.End);

await pipeline.DisposeAsync();

file sealed class SimpleDuplexPipe(PipeReader reader, PipeWriter writer) : IDuplexPipe
{
    public PipeReader Input => reader;
    public PipeWriter Output => writer;
}
