using FiberNet.Core;
using TUnit.Assertions.Extensions;

namespace FiberNet.Core.Tests;

public sealed class FiberAwaitableTests
{
    [Test]
    public async Task Complete_BeforeOnCompleted_ReturnsImmediately()
    {
        var awaitable = new FiberAwaitable();
        awaitable.Complete();

        await Assert.That(awaitable.IsCompleted).IsTrue();
    }

    [Test]
    public async Task OnCompleted_InvokesContinuation_WhenCompleted()
    {
        var awaitable = new FiberAwaitable();
        var invoked = false;

        awaitable.OnCompleted(() => invoked = true);
        awaitable.Complete();

        await Task.Delay(10); // let continuation fire
        await Assert.That(invoked).IsTrue();
    }
}
