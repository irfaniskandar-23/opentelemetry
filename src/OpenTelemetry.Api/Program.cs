using System.Collections.Concurrent;
using System.Diagnostics;
using OpenTelemetry.Exporter;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<ConcurrentDictionary<Guid, Store>>();

// AddProblemDetails registers the service that writes RFC 9457 bodies; without
// it the parameterless UseExceptionHandler below has no fallback and throws.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// The endpoint is not a credential and lives in appsettings.json; the token is
// one and lives in user secrets. Both arrive through the same Configuration
// object, which is the point — code never knows which layer answered.
//
// Throwing on a missing value is deliberate. A null token would produce an
// Authorization header of "Bearer ", a 401 inside the exporter's background
// thread, and no visible failure anywhere — the app would look healthy and
// export nothing.
var betterStackEndpoint = builder.Configuration["BetterStack:Endpoint"]
    ?? throw new InvalidOperationException("BetterStack:Endpoint is not configured.");
var betterStackToken = builder.Configuration["BetterStack:SourceToken"]
    ?? throw new InvalidOperationException("BetterStack:SourceToken is not configured. Set it with dotnet user-secrets.");

builder.Services.AddOpenTelemetry()
    // The Resource answers "who is reporting", and its attributes are attached
    // to every span this process exports. Without AddService the spans arrive
    // as "unknown_service:OpenTelemetry.Api" and cannot be filtered by service. 
    .ConfigureResource(resource => resource
        .AddService("store-api")

        // Which environment this process is. Read from ASPNETCORE_ENVIRONMENT,
        // which launchSettings.json sets to Development locally and which a host
        // like Azure App Service supplies as an application setting. Unset means
        // Production, deliberately — the safe default.
        //
        // A resource attribute, not a span tag: it describes the process, not
        // the request, and it is what keeps laptop traffic distinguishable from
        // real traffic when both export to the same source.
        .AddAttributes([new("deployment.environment.name", builder.Environment.EnvironmentName)]))
    .WithTracing(tracing => tracing
       // This is the SDK's ShouldListenTo, expressed as builder calls. The
       // hand-written listener deleted in this phase made the same decision
       // with a lambda; see docs/phase-1-activity.md.
       //
       // AddSource is not optional for the same reason it was not optional
       // there: an ActivitySource nobody subscribed to produces nothing, so
       // omitting this line silently drops every CreateStore span while
       // ASP.NET Core's request spans keep arriving — the most confusing
       // possible half-working state.
       .AddSource(Telemetry.SourceName) //root span

        // ASP.NET Core already emits a per-request Activity with zero packages;
        // phase 1 proved that. This subscribes to it and renames its data to
        // the OpenTelemetry semantic conventions (http.request.method, url.path)
        // that backends actually understand.
        .AddAspNetCoreInstrumentation(options =>
        {
            //Enrinchment hook
            options.EnrichWithHttpRequest = (activity, request) =>
            {
                activity.SetTag("http.request.body.size", request.ContentLength);
                activity.SetTag("http.request.header.content_type", request.ContentType);
                activity.SetTag("url.query", request.QueryString.Value);
                activity.SetTag("client.address", request.HttpContext.Connection.RemoteIpAddress?.ToString());
            };
        })

        .AddOtlpExporter(options =>
        {
            // Better Stack ingests traces at /v1/traces on the source's host.
            // The SDK would append that path itself, but writing it out means
            // the full destination is visible here rather than implied.
            options.Endpoint = new Uri($"{betterStackEndpoint}/v1/traces");

            // Two OTLP transports exist: gRPC and HTTP/protobuf. Better Stack
            // takes the HTTP one, and the default in this SDK is gRPC — so
            // leaving this out fails at the transport level, not with a
            // helpful message about the wrong protocol.
            options.Protocol = OtlpExportProtocol.HttpProtobuf;

            // The source token, sent as a normal HTTP header. Note the format:
            // a comma-separated "key=value" string, not a dictionary.
            options.Headers = $"Authorization=Bearer {betterStackToken}";
        }));

var app = builder.Build();

// Wraps everything registered after it in a try/catch, inside the developer
// exception page — so it catches first and the dev page never fires.
app.UseExceptionHandler(); //catch and call response.clear

// One middleware, one job: stamp the current trace onto every response so a
// caller can quote it in a bug report.
//
// Written BEFORE await next() on purpose. HTTP sends headers ahead of the body,
// so once the endpoint writes its first byte the headers are already on the
// wire. Move this line below await next() and it still compiles, still throws
// nothing, and silently changes nothing.
app.Use(async (context, next) =>
{
    // Activity.Id is already the W3C string — 00-<traceId>-<spanId>-<flags>.
    // Nothing to assemble by hand.
    var activity = Activity.Current;

    if (activity is not null)
    {
        // .TraceParent, not Headers["traceparent"]: well-known headers have
        // dedicated fields on IHeaderDictionary, so this skips the string hash
        // and a typo stops compiling. On the wire the name is still lowercase.
        context.Response.Headers.TraceParent = activity.Id;
    }

    await next();
});

app.MapPost("/stores", (CreateStoreRequest request, ConcurrentDictionary<Guid, Store> stores) =>
{
    var store = new Store(
        Guid.NewGuid(),
        request.Name,
        request.Address,
        Latitude: null,
        Longitude: null,
        DateTimeOffset.UtcNow);

    // A child span around the save. `using` matters: disposing the activity is
    // what stops the clock and fires ActivityStopped. Leave it out and the span
    // never ends.
    //
    // StartActivity returns Activity? — null when nobody is listening — so every
    // call below is null-conditional. Instrumented code must not crash an
    // uninstrumented process.
    using (var activity = Telemetry.Source.StartActivity("CreateStore")) //child span
    {
        // The point of the phase. This is a queryable field on the span, not
        // text inside a message. "Show me the trace for store X" becomes a
        // filter rather than a grep.
        activity?.SetTag("store.id", store.Id);
        activity?.SetTag("store.name", store.Name);

        stores[store.Id] = store;
    }

    return Results.Created($"/stores/{store.Id}", store);
});

app.MapGet("/stores/{id:guid}", (Guid id, ConcurrentDictionary<Guid, Store> stores) =>
    stores.TryGetValue(id, out var store)
        ? Results.Ok(store)
        : Results.NotFound());

app.MapGet("/stores", (ConcurrentDictionary<Guid, Store> stores) => stores.Values);

// Throws on purpose, to exercise the exception handler.
// The block body is required: a bare `throw` expression has no inferred return
// type, so overload resolution picks MapGet(string, RequestDelegate) instead.
app.MapGet("/stores/{id:guid}/boom", (Guid id) =>
{
    throw new InvalidOperationException($"Deliberate failure for store {id}.");
});

app.Run();

record Store(
    Guid Id,
    string Name,
    string Address,
    double? Latitude,
    double? Longitude,
    DateTimeOffset CreatedAt);

record CreateStoreRequest(string Name, string Address);
