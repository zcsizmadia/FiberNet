using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using FiberNet.Core;
using Microsoft.Extensions.Logging.Abstractions;

namespace FiberNet.Benchmarks;

[SimpleJob(RuntimeMoniker.Net80)]
[SimpleJob(RuntimeMoniker.Net90)]
[SimpleJob(RuntimeMoniker.Net10_0)]
[MemoryDiagnoser]
[HideColumns("Error", "StdDev", "Median", "RatioSD")]
public class FiberSchedulingBenchmark : IDisposable
{
    private FiberScheduler _scheduler = null!;

    [GlobalSetup]
    public void Setup() => _scheduler = new FiberScheduler(NullLogger<FiberScheduler>.Instance);

    [GlobalCleanup]
    public void Cleanup() => _scheduler.Dispose();

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _scheduler.Dispose();
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    [Benchmark(Description = "Spawn 1 000 fibers")]
    public void SpawnFibers()
    {
        var remaining = 1_000;
        using var done = new CountdownEvent(remaining);

        for (var i = 0; i < remaining; i++)
        {
            _scheduler.Spawn(() => done.Signal());
        }

        done.Wait();
    }
}
