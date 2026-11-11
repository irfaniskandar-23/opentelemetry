using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddConfiguration();
builder.Services.AddSingleton<ConcurrentDictionary<Guid, Store>>();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddGeocoding();
builder.Services.AddObservability(builder.Environment);
builder.Services.AddControllers();

await using var app = builder.Build();

app.UseExceptionHandler();
app.UseTraceParentHeader();
app.MapControllers();

try
{
    await app.StartAsync();
}
catch (Exception exception)
{
    app.Logger.LogCritical("Failure to start the program: {Reason}", exception.Message);
    return 1;
}

await app.WaitForShutdownAsync();
return 0;
