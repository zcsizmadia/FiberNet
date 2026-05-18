using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections;

namespace FiberNet.Transport.Kestrel;

/// <summary>
/// Drop-in Kestrel connection listener backed by the FiberNet fiber scheduler
/// and zero-copy pipeline. Register via <c>UseFiberNetTransport()</c>.
/// </summary>
public sealed class FiberNetConnectionListenerFactory : IConnectionListenerFactory
{
    public async ValueTask<IConnectionListener> BindAsync(
        EndPoint endpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        var listener = new FiberNetConnectionListener(endpoint);

        try
        {
            listener.Bind();
            return listener;
        }
        catch
        {
            await listener.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
