using System.Net;
using System.Net.Sockets;
using FiberNet.IO;

namespace FiberNet.IO.Linux;

/// <summary>
/// Linux SAEA I/O provider. Registered via DI on Linux/macOS hosts.
/// </summary>
public sealed class SaeaIOProvider : IFiberNetIOProvider
{
    public IFiberNetSocket CreateSocket()
    {
        var socket = new Socket(
            AddressFamily.InterNetwork,
            SocketType.Stream,
            ProtocolType.Tcp)
        {
            NoDelay = true,
        };

        return new SaeaSocket(socket);
    }

    public IFiberNetListener CreateListener(EndPoint endPoint)
    {
        ArgumentNullException.ThrowIfNull(endPoint);
        return new SaeaListener(endPoint);
    }
}
