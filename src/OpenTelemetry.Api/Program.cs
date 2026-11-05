using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddConfiguration();
builder.Services.AddSingleton<ConcurrentDictionary<Guid, Store>>();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddGeocoding();
builder.Services.AddObservability(builder.Environment);
builder.Services.AddControllers();

var app = builder.Build();

app.UseExceptionHandler();
app.UseTraceParentHeader();
app.MapControllers();

app.Run();
