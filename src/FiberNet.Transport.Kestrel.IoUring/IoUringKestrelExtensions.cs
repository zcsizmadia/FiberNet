using System;
using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.DependencyInjection;

namespace FiberNet.Transport.Kestrel.IoUring;

/// <summary>
/// Extension methods for registering the FiberNet io_uring Kestrel transport.
/// </summary>
public static class IoUringKestrelExtensions
{
    /// <summary>
    /// Replaces the default Kestrel socket transport with the FiberNet io_uring
    /// transport (Linux only, kernel >= 5.1).
    /// </summary>
    /// <exception cref="PlatformNotSupportedException">
    /// Thrown immediately on non-Linux platforms.
    /// </exception>
    public static IServiceCollection UseIoUringTransport(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            throw new PlatformNotSupportedException(
                "The io_uring transport is only supported on Linux (kernel >= 5.1).");
        }

        services.AddSingleton<IConnectionListenerFactory, IoUringConnectionListenerFactory>();
        return services;
    }
}
