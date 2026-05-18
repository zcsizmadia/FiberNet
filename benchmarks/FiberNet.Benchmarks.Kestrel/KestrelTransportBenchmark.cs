using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using FiberNet.Transport.Kestrel;
using System.Net;

namespace FiberNet.Benchmarks.Kestrel;

/// <summary>
/// Which Kestrel transport variant to exercise.
/// </summary>
public enum TransportKind
{
    /// <summary>Kestrel's built-in socket transport (baseline).</summary>
    Standard,

    /// <summary>FiberNet fiber-scheduled, zero-copy transport.</summary>
    FiberNet,
}

/// <summary>
/// Measures end-to-end HTTP/1.1 request throughput through two Kestrel transport
/// implementations — the default socket transport vs. the FiberNet transport layer.
///
/// Methodology:
///   • One minimal-API server is spun up per benchmark variant in <see cref="SetupAsync"/>.
///   • An <see cref="HttpClient"/> is pre-warmed (connection established) before BDN starts timing.
///   • The <see cref="SequentialRequests"/> benchmark fires <see cref="RequestCount"/>
///     keep-alive GET requests and reports mean latency + allocation per iteration.
///   • BDN launches a fresh out-of-process host per (runtime × transport) combination,
///     so the two servers never run concurrently and port 14 000 is always free.
/// </summary>
[SimpleJob(RuntimeMoniker.Net80)]
[SimpleJob(RuntimeMoniker.Net90)]
[SimpleJob(RuntimeMoniker.Net10_0)]
[MemoryDiagnoser]
[HideColumns("Error", "StdDev", "RatioSD")]
public class KestrelTransportBenchmark
{
    private const int Port = 14_000;
    private const int RequestCount = 200;

    private WebApplication _app = null!;
    private HttpClient _client = null!;

    /// <summary>Transport variant selected by BDN for this run.</summary>
    [Params(TransportKind.Standard, TransportKind.FiberNet)]
    public TransportKind Transport { get; set; }

    /// <summary>Starts the server and pre-warms the HTTP connection.</summary>
    [GlobalSetup]
    public async Task SetupAsync()
    {
        _app = BuildApp(useFiberNet: Transport == TransportKind.FiberNet);
        await _app.StartAsync().ConfigureAwait(false);

        _client = new HttpClient
        {
            BaseAddress = new Uri($"http://127.0.0.1:{Port}"),
            // Force HTTP/1.1 — no TLS required, keep-alive reuses the connection.
            DefaultRequestVersion = HttpVersion.Version11,
        };

        // Establish the TCP connection so timing begins in steady state.
        using var warmup = await _client.GetAsync(new Uri("/ping", UriKind.Relative)).ConfigureAwait(false);
        warmup.EnsureSuccessStatusCode();
    }

    /// <summary>Stops the server and disposes resources.</summary>
    [GlobalCleanup]
    public async Task CleanupAsync()
    {
        _client.Dispose();
        await _app.StopAsync().ConfigureAwait(false);
        await _app.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Sends <see cref="RequestCount"/> sequential HTTP GET requests over a single
    /// keep-alive connection and discards the response bodies.
    /// </summary>
    [Benchmark(Description = "Sequential GET ×200")]
    public async Task SequentialRequests()
    {
        for (var i = 0; i < RequestCount; i++)
        {
            using var response = await _client.GetAsync(new Uri("/ping", UriKind.Relative)).ConfigureAwait(false);
            _ = response.StatusCode;
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static WebApplication BuildApp(bool useFiberNet)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });

        // Silence framework startup noise so it doesn't skew timing output.
        builder.Logging.ClearProviders();

        builder.WebHost.ConfigureKestrel(o =>
        {
            o.ListenLocalhost(Port);
            // Minimise Kestrel's own allocation by disabling the request-body size limit.
            o.Limits.MaxRequestBodySize = null;
        });

        if (useFiberNet)
        {
            // Replace the default SocketTransportFactory with the FiberNet transport.
            // AddSingleton appends; Kestrel resolves IConnectionListenerFactory as the
            // last-registered service, so FiberNet wins over the socket transport.
            builder.Services.UseFiberNetTransport();
        }

        var app = builder.Build();

        // The server under test: a trivial "pong" response avoids body-parsing overhead
        // so we measure pure transport layer cost.
        app.MapGet("/ping", () => "pong");

        return app;
    }
}
