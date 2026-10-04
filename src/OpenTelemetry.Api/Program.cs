using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using OpenTelemetry.Exporter;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// Named once because three places need it: the registration, the resolve, and
// the enrichment hook that stamps it on the client span.
const string GeocodingClientName = "Geocoding";

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

// Nominatim needs no credential, so both values live in appsettings.json.
var geocodingBaseUrl = builder.Configuration["Geocoding:BaseUrl"]
    ?? throw new InvalidOperationException("Geocoding:BaseUrl is not configured.");
var geocodingUserAgent = builder.Configuration["Geocoding:UserAgent"]
    ?? throw new InvalidOperationException("Geocoding:UserAgent is not configured.");

// A named client, not `new HttpClient()`. The tracing reason is that this is
// where instrumentation expects to find outbound calls; the more important
// reason is that IHttpClientFactory pools and recycles the handler underneath,
// which is what prevents socket exhaustion and stale DNS.
//
// The User-Agent is not politeness. Nominatim answers 403 to generic clients —
// Postman's default is blocked — so without this line every call fails.
builder.Services.AddHttpClient(GeocodingClientName, client =>
{
    client.BaseAddress = new Uri(geocodingBaseUrl);
    client.DefaultRequestHeaders.UserAgent.ParseAdd(geocodingUserAgent);
});

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

        // The whole of phase 6, in one line. It creates the CLIENT span for
        // every outgoing HttpClient call and writes the traceparent header onto
        // the request — no propagation code anywhere in this file.
        //
        // On .NET 9+ the runtime already emits this activity from its own
        // System.Net.Http source; the package still earns its place for context
        // propagation plus the enrichment and filtering hooks below.
        .AddHttpClientInstrumentation(options =>
        {
            // Which registered client made the call. The semantic conventions
            // describe the wire (server.address, url.full) but not our own
            // naming, so this is exactly the kind of gap enrichment is for.
            //
            // Only runs when the activity is sampled — an enricher that looks
            // dead is usually attached to an unsampled span.
            options.EnrichWithHttpRequestMessage = (activity, _) =>
                activity.SetTag("http.client.name", GeocodingClientName);
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

app.MapPost("/stores", async (
    CreateStoreRequest request,
    ConcurrentDictionary<Guid, Store> stores,
    IHttpClientFactory httpClientFactory,
    CancellationToken cancellationToken) =>
{
    // A child span around the whole operation. `using` matters: disposing the
    // activity is what stops the clock and fires ActivityStopped. Leave it out
    // and the span never ends.
    //
    // StartActivity returns Activity? — null when nobody is listening — so every
    // call below is null-conditional. Instrumented code must not crash an
    // uninstrumented process.
    //
    // The geocoding call happens inside this scope on purpose. Activity.Current
    // is ambient, so whatever is current when HttpClient runs becomes the client
    // span's parent. Move the call above this line and the client span reparents
    // to the request span, and the waterfall stops telling the truth about which
    // operation made it.
    using var activity = Telemetry.Source.StartActivity("CreateStore"); //child span

    // url.full redacts the query string, so the address is only searchable if
    // it is set here deliberately, having decided it is not sensitive.
    activity?.SetTag("store.address", request.Address);

    var geocodingClient = httpClientFactory.CreateClient(GeocodingClientName);

    // Relative URI — the base address came from configuration at registration.
    var response = await geocodingClient.GetAsync(
        $"search?q={Uri.EscapeDataString(request.Address)}&format=json&limit=1",
        cancellationToken);

    // A 403 from a missing User-Agent throws here, reaching
    // GlobalExceptionHandler exactly like the /boom endpoint does. Phase 3
    // already built the error path; this phase adds a real way to trigger it.
    response.EnsureSuccessStatusCode();

    var matches = await response.Content
        .ReadFromJsonAsync<GeocodingMatch[]>(cancellationToken) ?? [];

    // Distinguishes "no match" from "matched something" without storing the
    // response body on the span.
    activity?.SetTag("geocode.match_count", matches.Length);

    // Nominatim answers an unresolvable address with an empty array and HTTP
    // 200, so this is a success status that is not a success.
    if (matches.Length == 0)
    {
        throw new InvalidOperationException(
            $"No coordinates found for address '{request.Address}'.");
    }

    // Coordinates arrive as strings. InvariantCulture is not optional — under a
    // comma-decimal locale double.Parse would read "52.5170365" as 525170365.
    var latitude = double.Parse(matches[0].Lat, CultureInfo.InvariantCulture);
    var longitude = double.Parse(matches[0].Lon, CultureInfo.InvariantCulture);

    var store = new Store(
        Guid.NewGuid(),
        request.Name,
        request.Address,
        latitude,
        longitude,
        DateTimeOffset.UtcNow);

    // The point of phase 2. These are queryable fields on the span, not text
    // inside a message. "Show me the trace for store X" becomes a filter rather
    // than a grep.
    activity?.SetTag("store.id", store.Id);
    activity?.SetTag("store.name", store.Name);

    // Set here rather than in EnrichWithHttpResponseMessage: these values come
    // from the response body, and reading the content stream inside that
    // synchronous callback would interfere with this code reading it properly.
    activity?.SetTag("geocode.latitude", latitude);
    activity?.SetTag("geocode.longitude", longitude);

    stores[store.Id] = store;

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

// Only the two fields this project needs. Nominatim returns roughly twenty more
// per match; deserialising the whole payload would invite putting it on a span.
// Lat and Lon are strings in the JSON, not numbers.
record GeocodingMatch(string Lat, string Lon);
