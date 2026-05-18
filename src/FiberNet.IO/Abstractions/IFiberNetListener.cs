using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace FiberNet.IO;

/// <summary>
/// A fiber-aware, zero-alloc TCP listener abstraction.
/// </summary>
public interface IFiberNetListener : IAsyncDisposable
{
    EndPoint LocalEndPoint { get; }
    void Bind();
    void Listen(int backlog = 128);
    ValueTask<IFiberNetSocket> AcceptAsync(CancellationToken cancellationToken = default);
}
