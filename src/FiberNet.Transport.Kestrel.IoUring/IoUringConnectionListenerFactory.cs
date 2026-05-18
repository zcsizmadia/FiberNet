using System;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using FiberNet.Transport.Kestrel.IoUring.Interop;
using Microsoft.AspNetCore.Connections;

namespace FiberNet.Transport.Kestrel.IoUring;

/// <summary>
/// Drop-in Kestrel connection listener factory backed by io_uring (Linux only).
/// Register via <c>UseIoUringTransport()</c>.
/// </summary>
public sealed class IoUringConnectionListenerFactory : IConnectionListenerFactory
{
    /// <inheritdoc />
    public ValueTask<IConnectionListener> BindAsync(
        EndPoint endpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            throw new PlatformNotSupportedException(
                "The io_uring transport is only supported on Linux (kernel >= 5.1).");
        }

        if (endpoint is not IPEndPoint ipEndPoint)
        {
            throw new ArgumentException(
                "The io_uring transport requires an IPEndPoint.", nameof(endpoint));
        }

        var serverFd = IoUringInterop.CreateServerSocket(ipEndPoint.Port, 512);
        if (serverFd < 0)
        {
            throw new IOException(
                $"Failed to create server socket on port {ipEndPoint.Port}: errno={-serverFd}");
        }

        var ops  = new IoUringPendingOps();
        IoUringRing? ring = null;

        try
        {
            ring = new IoUringRing(ops);
            IConnectionListener listener = new IoUringConnectionListener(endpoint, serverFd, ring);
            return ValueTask.FromResult(listener);
        }
        catch
        {
            ring?.Dispose();
            ops.Dispose();
            _ = IoUringInterop.CloseFd(serverFd);
            throw;
        }
    }
}
