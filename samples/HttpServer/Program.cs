using FiberNet.Transport.Kestrel;

var builder = WebApplication.CreateBuilder(args);
builder.Services.UseFiberNetTransport();

var app = builder.Build();
app.MapGet("/", () => "Hello from FiberNet!");

app.Run();
