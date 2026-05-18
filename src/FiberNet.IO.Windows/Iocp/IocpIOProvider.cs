using System.Net;
using FiberNet.IO;

namespace FiberNet.IO.Windows;

/// <summary>
/// Windows IOCP I/O provider. Registered via DI on Windows hosts.
/// </summary>
public sealed class IocpIOProvider : IFiberNetIOProvider
{
    public IFiberNetSocket CreateSocket()
    {
        var socket = new System.Net.Sockets.Socket(
            System.Net.Sockets.AddressFamily.InterNetwork,
            System.Net.Sockets.SocketType.Stream,
            System.Net.Sockets.ProtocolType.Tcp)
        {
            NoDelay = true,
        };

        return new IocpSocket(socket);
    }

    public IFiberNetListener CreateListener(EndPoint endPoint)
    {
        ArgumentNullException.ThrowIfNull(endPoint);
        return new IocpListener(endPoint);
    }
}
