using System;
using System.Net;

namespace FiberNet.IO;

/// <summary>
/// Platform-neutral factory for fiber-aware sockets and listeners.
/// </summary>
public interface IFiberNetIOProvider
{
    IFiberNetSocket CreateSocket();
    IFiberNetListener CreateListener(EndPoint endPoint);
}
