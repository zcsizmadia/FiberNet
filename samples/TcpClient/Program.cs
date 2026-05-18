using System.Net;
using System.Text;
using FiberNet.IO.Linux;

var provider = new SaeaIOProvider();
await using var socket = provider.CreateSocket();

await socket.ConnectAsync(new IPEndPoint(IPAddress.Loopback, 5000));

var message = Encoding.UTF8.GetBytes("Hello FiberNet!");
await socket.SendAsync(message);

var buffer = new byte[4096];
var read = await socket.ReceiveAsync(buffer);

Console.WriteLine($"Echo: {Encoding.UTF8.GetString(buffer, 0, read)}");
socket.Shutdown();
