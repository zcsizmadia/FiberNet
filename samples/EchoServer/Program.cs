using System.Net;
using FiberNet.IO.Linux;
using Microsoft.Extensions.Logging.Abstractions;

// Platform-neutral: use SAEA on all platforms for simplicity in this sample.
// On Windows, swap SaeaIOProvider for IocpIOProvider from FiberNet.IO.Windows.
var provider = new SaeaIOProvider();
var endpoint = new IPEndPoint(IPAddress.Loopback, 5000);

await using var listener = provider.CreateListener(endpoint);
listener.Bind();
listener.Listen();

Console.WriteLine($"Echo server listening on {endpoint}");

while (true)
{
    var socket = await listener.AcceptAsync();
    _ = HandleAsync(socket);
}

static async Task HandleAsync(FiberNet.IO.IFiberNetSocket socket)
{
    var buffer = new byte[4096];

    try
    {
        int read;

        while ((read = await socket.ReceiveAsync(buffer)) > 0)
        {
            await socket.SendAsync(buffer.AsMemory(0, read));
        }
    }
    finally
    {
        socket.Shutdown();
        await socket.DisposeAsync();
    }
}
