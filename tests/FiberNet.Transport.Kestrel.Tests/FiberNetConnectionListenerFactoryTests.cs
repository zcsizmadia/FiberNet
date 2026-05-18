using FiberNet.Transport.Kestrel;
using Microsoft.AspNetCore.Connections;
using TUnit.Assertions.Extensions;

namespace FiberNet.Transport.Kestrel.Tests;

public sealed class FiberNetConnectionListenerFactoryTests
{
    [Test]
    public async Task BindAsync_ReturnsListener()
    {
        var factory = new FiberNetConnectionListenerFactory();
        var endpoint = new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0);

        await using var listener = await factory.BindAsync(endpoint);

        await Assert.That(listener).IsNotNull();
        await listener.DisposeAsync();
    }
}
