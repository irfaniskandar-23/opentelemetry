using System.Collections.Concurrent;
using System.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<ConcurrentDictionary<Guid, Store>>();

// AddProblemDetails registers the service that writes RFC 9457 bodies; without
// it the parameterless UseExceptionHandler below has no fallback and throws.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// .NET only materialises activities when something is listening. Without this
// listener, ASP.NET Core's per-request Activity is never created and the code
// below prints nothing. No package is involved: Activity, ActivitySource and
// ActivityListener all live in System.Diagnostics.
var listener = new ActivityListener
{
    // Subscribe to ASP.NET Core's source and our own. The whole process is full
    // of other sources — the TLS stack, sockets, the runtime — and listening to
    // all of them buries the request spans in noise. Swap in `_ => true` to
    // see everything the runtime records.
    //
    // The second clause is not optional. Without it Telemetry.Source is a source
    // nobody subscribed to, StartActivity returns null, and the CreateStore span
    // below silently never exists.
    ShouldListenTo = source =>
        source.Name.StartsWith("Microsoft.AspNetCore")
        || source.Name == Telemetry.SourceName,

    // AllData means "create the activity and populate its tags". Anything less
    // and the activity is either hollow or skipped entirely.
    //
    // AndRecorded is what sets the Recorded trace flag, which is the last byte
    // of the traceparent header: 01 sampled, 00 not. Plain AllData populates the
    // activity locally but emits -00, telling every downstream service "don't
    // bother recording this trace" — the opposite of what we mean.
    Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,

    // traceId, then parentId, then spanId — the order the tree is read in:
    // which journey, who called me, who I am.
    ActivityStarted = activity =>
        Console.WriteLine(
            $"[start] {activity.DisplayName} kind={activity.Kind} " +
            $"traceId={activity.TraceId} parentId={activity.ParentSpanId} spanId={activity.SpanId}"),

    ActivityStopped = activity =>
    {
        var tags = string.Join(", ", activity.TagObjects.Select(tag => $"{tag.Key}={tag.Value}"));

        // AddException records an event, not a tag, so events need their own
        // line here or the exception is invisible in this console output.
        var events = string.Join(", ", activity.Events.Select(e => e.Name));

        Console.WriteLine(
            $"[stop ] {activity.DisplayName} kind={activity.Kind} " +
            $"traceId={activity.TraceId} parentId={activity.ParentSpanId} spanId={activity.SpanId} " +
            $"duration={activity.Duration.TotalMilliseconds:F1}ms status={activity.Status} " +
            $"tags=[{tags}] events=[{events}]");

        foreach (var activityEvent in activity.Events)
        {
            foreach (var tag in activityEvent.Tags)
            {
                Console.WriteLine($"         {tag.Key}={tag.Value}");
            }
        }
    }
};

ActivitySource.AddActivityListener(listener);

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
    using (var activity = Telemetry.Source.StartActivity("CreateStore"))
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
