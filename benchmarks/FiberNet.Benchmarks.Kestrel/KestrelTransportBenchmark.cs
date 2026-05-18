using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using FiberNet.Transport.Kestrel;
using FiberNet.Transport.Kestrel.IoUring;

namespace FiberNet.Benchmarks.Kestrel;

internal static class LoadBench
{
    private const int Port          = 14_000;
    private const int WarmupSeconds = 3;
    private const int RunSeconds    = 10;
    private const int DefaultConnections = 16;

    // BodySize == 0  →  GET;  BodySize > 0  →  POST with that payload
    private static readonly Scenario[] s_scenarios =
    [
        new("PING",      "/ping",       0),
        new("GET  512B", "/data/512",   0),
        new("GET   4KB", "/data/4096",  0),
        new("GET  64KB", "/data/65536", 0),
        new("POST 512B", "/echo",       512),
        new("POST  4KB", "/echo",       4_096),
        new("POST 64KB", "/echo",       65_536),
    ];

    public static async Task RunAsync(string[] args)
    {
        var connections = ParseConnections(args);

        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine(
            $"FiberNet Kestrel Load Bench  ·  {connections} keep-alive connections  " +
            $"·  {WarmupSeconds}s warmup  ·  {RunSeconds}s measurement");

        var stdResults = await BenchAllAsync(TransportVariant.Standard, "Standard", connections).ConfigureAwait(false);
        var fnResults  = await BenchAllAsync(TransportVariant.FiberNet,  "FiberNet",  connections).ConfigureAwait(false);

        BenchResult[]? iouResults = null;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            Console.WriteLine("\nLinux detected — benchmarking io_uring transport…");
            iouResults = await BenchAllAsync(TransportVariant.IoUring, "IoUring", connections).ConfigureAwait(false);
        }

        PrintComparison(stdResults, fnResults, iouResults);
    }

    private static int ParseConnections(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if ((args[i] is "--connections" or "-c") &&
                int.TryParse(args[i + 1], out var n) && n > 0)
            {
                return n;
            }
        }

        return DefaultConnections;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Transport loop
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task<BenchResult[]> BenchAllAsync(TransportVariant transport, string label, int connections)
    {
        var app = BuildServer(transport);
        await app.StartAsync().ConfigureAwait(false);
        Console.WriteLine($"\n[{label}] server ready on port {Port}");

        var results = new BenchResult[s_scenarios.Length];
        try
        {
            for (var i = 0; i < s_scenarios.Length; i++)
            {
                var sc = s_scenarios[i];
                Console.Write($"  {sc.Name,-12} running...");
                results[i] = await RunScenarioAsync(sc, connections).ConfigureAwait(false);
                Console.Write(
                    $"\r  {sc.Name,-12}  {results[i].Rps,9:N0} req/s  " +
                    $"P50={results[i].P50Ms:F2}ms              \n");
            }
        }
        finally
        {
            await app.StopAsync().ConfigureAwait(false);
            await app.DisposeAsync().ConfigureAwait(false);
        }

        return results;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Load driver
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task<BenchResult> RunScenarioAsync(Scenario scenario, int connections)
    {
        var clients = CreateClients(connections);

        byte[]? postBody = null;
        if (scenario.BodySize > 0)
        {
            postBody = new byte[scenario.BodySize];
            RandomNumberGenerator.Fill(postBody);
        }

        var warmupEnd  = Stopwatch.GetTimestamp() + (long)(Stopwatch.Frequency * WarmupSeconds);
        var measureEnd = warmupEnd + (long)(Stopwatch.Frequency * RunSeconds);

        long totalRequests = 0;
        long totalBytes    = 0;

        // One task per keep-alive connection – loop until measureEnd
        var tasks = clients.Select(client => Task.Run(async () =>
        {
            var localLatencies = new List<long>();
            var localBytes     = 0L;

            while (Stopwatch.GetTimestamp() < measureEnd)
            {
                var measuring = Stopwatch.GetTimestamp() >= warmupEnd;
                var t0        = Stopwatch.GetTimestamp();

                try
                {
                    var bytes = await SendRequestAsync(client, scenario.Path, postBody)
                        .ConfigureAwait(false);

                    if (measuring)
                    {
                        localLatencies.Add(Stopwatch.GetTimestamp() - t0);
                        localBytes += bytes;
                    }
                }
                catch (HttpRequestException) { /* skip transient errors */ }
            }

            Interlocked.Add(ref totalRequests, localLatencies.Count);
            Interlocked.Add(ref totalBytes,    localBytes);
            return localLatencies;
        })).ToArray();

        var lists    = await Task.WhenAll(tasks).ConfigureAwait(false);
        var allTicks = lists.SelectMany(static l => l).ToArray();
        Array.Sort(allTicks);

        foreach (var c in clients)
        {
            c.Dispose();
        }

        return new BenchResult(totalRequests, totalBytes, RunSeconds, allTicks);
    }

    private static async Task<long> SendRequestAsync(
        HttpClient client, string path, byte[]? postBody)
    {
        var uri = new Uri(path, UriKind.Relative);

        if (postBody is null)
        {
            using var resp = await client
                .GetAsync(uri, HttpCompletionOption.ResponseHeadersRead)
                .ConfigureAwait(false);
            var contentLength = resp.Content.Headers.ContentLength ?? 0L;
            await resp.Content.CopyToAsync(Stream.Null, CancellationToken.None)
                .ConfigureAwait(false);
            return contentLength;
        }

        var handler = new ByteArrayContent(postBody);
        using var req  = new HttpRequestMessage(HttpMethod.Post, uri) { Content = handler };
        using var post = await client
            .SendAsync(req, HttpCompletionOption.ResponseHeadersRead)
            .ConfigureAwait(false);
        await post.Content.CopyToAsync(Stream.Null, CancellationToken.None)
            .ConfigureAwait(false);
        // count both request body (sent) + echo response (received)
        return postBody.Length + (post.Content.Headers.ContentLength ?? 0L);
    }

    private static HttpClient[] CreateClients(int connections) =>
        Enumerable.Range(0, connections).Select(_ =>
        {
            var handler = new SocketsHttpHandler
            {
                MaxConnectionsPerServer     = 1,
                PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan,
            };
            return new HttpClient(handler)
            {
                BaseAddress           = new Uri($"http://127.0.0.1:{Port}"),
                DefaultRequestVersion = HttpVersion.Version11,
                DefaultVersionPolicy  = HttpVersionPolicy.RequestVersionExact,
            };
        }).ToArray();

    // ─────────────────────────────────────────────────────────────────────────
    // Server
    // ─────────────────────────────────────────────────────────────────────────

    private static WebApplication BuildServer(TransportVariant transport)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(o =>
        {
            o.Listen(IPAddress.Loopback, Port);
            o.Limits.MaxRequestBodySize       = null;
            o.Limits.MaxConcurrentConnections = null;
        });
        if (transport == TransportVariant.FiberNet)
        {
            builder.Services.UseFiberNetTransport();
        }
        else if (transport == TransportVariant.IoUring)
        {
            builder.Services.UseIoUringTransport();
        }

        var app = builder.Build();

        // Pre-allocate static GET payloads — no per-request allocation on the hot path.
        var get512 = new byte[512];
        var get4K  = new byte[4_096];
        var get64K = new byte[65_536];

        app.MapGet("/ping",       ()               => "pong");
        app.MapGet("/data/512",   ()               => Results.Bytes(get512,  "application/octet-stream"));
        app.MapGet("/data/4096",  ()               => Results.Bytes(get4K,   "application/octet-stream"));
        app.MapGet("/data/65536", ()               => Results.Bytes(get64K,  "application/octet-stream"));
        app.MapPost("/echo", async (HttpRequest r) =>
        {
            using var ms = new MemoryStream((int)(r.ContentLength ?? 0));
            await r.Body.CopyToAsync(ms, CancellationToken.None).ConfigureAwait(false);
            return Results.Bytes(ms.ToArray(), "application/octet-stream");
        });

        return app;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Output
    // ─────────────────────────────────────────────────────────────────────────

    private static void PrintComparison(BenchResult[] std, BenchResult[] fn, BenchResult[]? ioU)
    {
        const int cols = 86;
        Console.WriteLine();
        Console.WriteLine(
            $"{"Scenario",-12}  {"Transport",-9}  {"Req/s",9}  {"BW MB/s",8}  " +
            $"{"P50 ms",7}  {"P90 ms",7}  {"P99 ms",7}");
        Console.WriteLine(new string('─', cols));

        for (var i = 0; i < s_scenarios.Length; i++)
        {
            PrintRow(s_scenarios[i].Name, "Standard", std[i]);
            PrintRow("",                  "FiberNet",  fn[i]);
            if (ioU is not null)
            {
                PrintRow("", "IoUring", ioU[i]);
            }

            var deltaFn = std[i].Rps > 0 ? (fn[i].Rps - std[i].Rps) / std[i].Rps * 100.0 : 0.0;
            var signFn  = deltaFn >= 0 ? "+" : "";
            Console.WriteLine($"{""  ,12}  {"  Δ FiberNet",-12}  {signFn}{deltaFn:F1}%");
            if (ioU is not null)
            {
                var deltaIou = std[i].Rps > 0 ? (ioU[i].Rps - std[i].Rps) / std[i].Rps * 100.0 : 0.0;
                var signIou  = deltaIou >= 0 ? "+" : "";
                Console.WriteLine($"{""  ,12}  {"  Δ IoUring",-12}  {signIou}{deltaIou:F1}%");
            }

            Console.WriteLine();
        }
    }

    private static void PrintRow(string scenario, string transport, BenchResult r) =>
        Console.WriteLine(
            $"{scenario,-12}  {transport,-9}  {r.Rps,9:N0}  {r.BandwidthMBps,8:F2}  " +
            $"{r.P50Ms,7:F2}  {r.P90Ms,7:F2}  {r.P99Ms,7:F2}");
}

// ─────────────────────────────────────────────────────────────────────────────

internal enum TransportVariant { Standard, FiberNet, IoUring }

internal sealed record Scenario(string Name, string Path, int BodySize);

internal readonly struct BenchResult
{
    public double Rps           { get; }
    public double BandwidthMBps { get; }
    public double P50Ms         { get; }
    public double P90Ms         { get; }
    public double P99Ms         { get; }

    public BenchResult(long requests, long bytes, int seconds, long[] sortedTicks)
    {
        Rps           = (double)requests / seconds;
        BandwidthMBps = (double)bytes / seconds / (1024.0 * 1024.0);
        if (sortedTicks.Length == 0)
        {
            return;
        }
        P50Ms = TicksToMs(sortedTicks[(int)(sortedTicks.Length * 0.50)]);
        P90Ms = TicksToMs(sortedTicks[(int)(sortedTicks.Length * 0.90)]);
        P99Ms = TicksToMs(sortedTicks[(int)(sortedTicks.Length * 0.99)]);
    }

    private static double TicksToMs(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
}
