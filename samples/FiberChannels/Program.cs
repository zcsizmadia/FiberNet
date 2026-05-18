using FiberNet.Core;
using Microsoft.Extensions.Logging.Abstractions;

var channel = new FiberChannel<string>();
using var scheduler = new FiberScheduler(NullLogger<FiberScheduler>.Instance);

// Producer fiber
scheduler.Spawn(() =>
{
    for (var i = 0; i < 5; i++)
    {
        channel.Write($"Message {i}");
    }
});

// Consumer (main thread reads for demo clarity)
await Task.Delay(100); // let producer run

while (channel.TryRead(out var msg))
{
    Console.WriteLine($"Received: {msg}");
}
