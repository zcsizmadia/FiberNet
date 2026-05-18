using FiberNet.Transport.Kestrel;

var builder = WebApplication.CreateBuilder(args);

// Replace default Kestrel transport with FiberNet
builder.Services.UseFiberNetTransport();

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new { runtime = "FiberNet", transport = "fiber-scheduled" }));
app.MapGet("/ping", () => "pong");

app.Run();
