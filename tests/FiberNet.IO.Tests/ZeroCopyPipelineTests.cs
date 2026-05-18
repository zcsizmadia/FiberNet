using System.IO.Pipelines;
using FiberNet.IO;
using TUnit.Assertions.Extensions;

namespace FiberNet.IO.Tests;

internal sealed class ZeroCopyPipelineTests
{
    [Test]
    public async Task DisposeAsync_MarksOutputAsCompleted()
    {
        var pipe = new Pipe();
        var duplex = new MockDuplexPipe(pipe.Reader, pipe.Writer);
        var pipeline = new ZeroCopyPipeline(duplex);

        await pipeline.DisposeAsync();

        // After the output writer is completed, flushing returns IsCompleted = true.
        var flush = await pipe.Writer.FlushAsync();
        await Assert.That(flush.IsCompleted).IsTrue();
    }

    private sealed class MockDuplexPipe(PipeReader reader, PipeWriter writer) : IDuplexPipe
    {
        public PipeReader Input => reader;
        public PipeWriter Output => writer;
    }
}
