using FiberNet.Core;
using Microsoft.Extensions.Logging.Abstractions;
using TUnit.Assertions.Extensions;

namespace FiberNet.Core.Tests;

public sealed class FiberSchedulerTests
{
    [Test]
    public async Task Spawn_ExecutesContinuation()
    {
        using var scheduler = new FiberScheduler(NullLogger<FiberScheduler>.Instance);
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        scheduler.Spawn(() => tcs.TrySetResult(true));

        var result = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task Spawn_MultipleFibers_AllExecute()
    {
        using var scheduler = new FiberScheduler(NullLogger<FiberScheduler>.Instance);
        var counter = 0;
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        const int Count = 100;

        for (var i = 0; i < Count; i++)
        {
            scheduler.Spawn(() =>
            {
                Interlocked.Increment(ref counter);

                if (Volatile.Read(ref counter) == Count)
                {
                    tcs.TrySetResult();
                }
            });
        }

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.That(counter).IsEqualTo(Count);
    }

    [Test]
    public void Spawn_AfterDispose_ThrowsObjectDisposedException()
    {
        var scheduler = new FiberScheduler(NullLogger<FiberScheduler>.Instance);
        scheduler.Dispose();

        Assert.Throws<ObjectDisposedException>(() => scheduler.Spawn(() => { }));
    }
}
