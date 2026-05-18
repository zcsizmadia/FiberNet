using FiberNet.Core;
using TUnit.Assertions.Extensions;

namespace FiberNet.Core.Tests;

public sealed class FiberChannelTests
{
    [Test]
    public async Task Write_ThenTryRead_ReturnsItem()
    {
        var channel = new FiberChannel<int>();
        channel.Write(42);

        var success = channel.TryRead(out var value);

        await Assert.That(success).IsTrue();
        await Assert.That(value).IsEqualTo(42);
    }

    [Test]
    public async Task WaitToReadAsync_AlreadyHasItem_CompletesImmediately()
    {
        var channel = new FiberChannel<string>();
        channel.Write("hello");

        FiberChannelAwaitable<string> awaitable = channel.WaitToReadAsync();

        await Assert.That(awaitable.IsCompleted).IsTrue();
    }

    [Test]
    public async Task WaitToReadAsync_NoItem_CompletesAfterWrite()
    {
        var channel = new FiberChannel<int>();
        FiberChannelAwaitable<int> awaitable = channel.WaitToReadAsync();

        await Assert.That(awaitable.IsCompleted).IsFalse();

        channel.Write(1);

        await Assert.That(awaitable.IsCompleted).IsTrue();
    }
}
