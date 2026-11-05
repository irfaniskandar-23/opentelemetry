using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddConfiguration();

builder.Services.AddSingleton<ConcurrentDictionary<Guid, Store>>();

// AddProblemDetails registers the service that writes RFC 9457 bodies; without
// it the parameterless UseExceptionHandler below has no fallback and throws.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddGeocoding();
builder.Services.AddObservability(builder.Environment);

var app = builder.Build();

// Wraps everything registered after it in a try/catch, inside the developer
// exception page — so it catches first and the dev page never fires.
app.UseExceptionHandler(); //catch and call response.clear

app.UseTraceParentHeader();

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

    // The point of phase 2. These are queryable fields on the span, not text
    // inside a message. "Show me the trace for store X" becomes a filter rather
    // than a grep.
    //
    // Both are set before the call, not after it, so a failed request still
    // carries the whole payload that caused it.
    //
    // url.full redacts the query string, so the address is only searchable if
    // it is set here deliberately, having decided it is not sensitive.
    activity?.SetTag("store.name", request.Name);
    activity?.SetTag("store.address", request.Address);

    try
    {
        var geocodingClient = httpClientFactory.CreateClient(GeocodingExtensions.ClientName);

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

        // The id only exists once the store does, so it cannot be set earlier.
        activity?.SetTag("store.id", store.Id);

        // Set here rather than in EnrichWithHttpResponseMessage: these values come
        // from the response body, and reading the content stream inside that
        // synchronous callback would interfere with this code reading it properly.
        activity?.SetTag("geocode.latitude", latitude);
        activity?.SetTag("geocode.longitude", longitude);

        stores[store.Id] = store;

        return Results.Created($"/stores/{store.Id}", store);
    }
    catch (Exception ex)
    {
        // Mark CreateStore itself as failed. Without this the exception leaves
        // the `using` scope, CreateStore is disposed with status Unset, and by
        // the time GlobalExceptionHandler reads Activity.Current it is back on
        // the request span — so the error and store.address end up on different
        // spans, and "failed spans for this address" finds nothing.
        //
        // Status and error.type only, no AddException. The semantic conventions
        // say a propagated exception is recorded once; GlobalExceptionHandler
        // already does that at the boundary that handles it.
        // https://opentelemetry.io/docs/specs/semconv/general/recording-errors/
        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        activity?.SetTag("error.type", ex.GetType().FullName);

        // `throw;`, not `throw ex;` — the bare form keeps the original stack
        // trace, which is what the handler records.
        throw;
    }
});

// ILogger<Program> is already registered by CreateBuilder, the same way
// IHttpClientFactory is. <Program> is the category — the name printed beside
// each line in the terminal.
app.MapGet("/stores/{id:guid}", (
    Guid id,
    ConcurrentDictionary<Guid, Store> stores,
    ILogger<Program> logger) =>
{
    // No child span here, so Activity.Current is ASP.NET Core's request span.
    // Tagging it is the phase 2 technique on an existing span: without this the
    // id lives only inside url.path, and "filter traces by store.id" finds
    // nothing for reads.
    Activity.Current?.SetTag("store.id", id);

    // Generic messages on purpose: no {StoreId}. Identity is on the span; the
    // trace ID the logging provider copies from Activity.Current is what joins
    // these lines to it.
    if (stores.TryGetValue(id, out var store))
    {
        logger.LogInformation("Store found");
        return Results.Ok(store);
    }

    // A failure with no exception — nothing marks the span as an error, so this
    // line is the only record of why the 404 happened.
    logger.LogWarning("Store not found");
    return Results.NotFound();
});

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
