using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddConfiguration();

builder.Services.AddSingleton<ConcurrentDictionary<Guid, Store>>();

// AddProblemDetails registers the service that writes RFC 9457 bodies; without
// it the parameterless UseExceptionHandler below has no fallback and throws.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddControllers();
builder.Services.AddGeocoding();
builder.Services.AddObservability(builder.Environment);

var app = builder.Build();

// Wraps everything registered after it in a try/catch, inside the developer
// exception page — so it catches first and the dev page never fires.
app.UseExceptionHandler(); //catch and call response.clear

app.UseTraceParentHeader();

app.MapControllers();

app.Run();
