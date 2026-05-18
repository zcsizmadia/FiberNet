using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using FiberNet.Core;

namespace FiberNet.Benchmarks;

[SimpleJob(RuntimeMoniker.Net80)]
[SimpleJob(RuntimeMoniker.Net90)]
[SimpleJob(RuntimeMoniker.Net10_0)]
[MemoryDiagnoser]
[HideColumns("Error", "StdDev", "Median", "RatioSD")]
public class FiberChannelBenchmark
{
    private FiberChannel<int> _channel = null!;

    [GlobalSetup]
    public void Setup() => _channel = new FiberChannel<int>();

    [Benchmark(Description = "Write + TryRead 10 000 items")]
    public void WriteAndRead()
    {
        for (var i = 0; i < 10_000; i++)
        {
            _channel.Write(i);
            _channel.TryRead(out _);
        }
    }
}
