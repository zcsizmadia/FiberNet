using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.DependencyInjection;

namespace FiberNet.Transport.Kestrel;

/// <summary>
/// Extension methods for registering the FiberNet Kestrel transport.
/// </summary>
public static class FiberNetKestrelExtensions
{
    /// <summary>
    /// Replaces the default Kestrel socket transport with the FiberNet fiber-scheduled transport.
    /// </summary>
    public static IServiceCollection UseFiberNetTransport(this IServiceCollection services)
    {
        services.AddSingleton<IConnectionListenerFactory, FiberNetConnectionListenerFactory>();
        return services;
    }
}
