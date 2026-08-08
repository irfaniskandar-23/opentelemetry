using System.Collections.Concurrent;
using System.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<ConcurrentDictionary<Guid, Store>>();

// .NET only materialises activities when something is listening. Without this
// listener, ASP.NET Core's per-request Activity is never created and the code
// below prints nothing. No package is involved: Activity, ActivitySource and
// ActivityListener all live in System.Diagnostics.
var listener = new ActivityListener
{
    // Subscribe to ASP.NET Core's source only. The whole process is full of
    // other sources — the TLS stack, sockets, the runtime — and listening to
    // all of them buries the request spans in noise. Swap in `_ => true` to
    // see everything the runtime records.
    ShouldListenTo = source => source.Name.StartsWith("Microsoft.AspNetCore"),

    // AllData means "create the activity and populate its tags". Anything less
    // and the activity is either hollow or skipped entirely.
    Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,

    // traceId, then parentId, then spanId — the order the tree is read in:
    // which journey, who called me, who I am.
    ActivityStarted = activity =>
        Console.WriteLine(
            $"[start] {activity.DisplayName} kind={activity.Kind} " +
            $"traceId={activity.TraceId} parentId={activity.ParentSpanId} spanId={activity.SpanId}"),

    ActivityStopped = activity =>
    {
        var tags = string.Join(", ", activity.TagObjects.Select(tag => $"{tag.Key}={tag.Value}"));
        Console.WriteLine(
            $"[stop ] {activity.DisplayName} kind={activity.Kind} " +
            $"traceId={activity.TraceId} parentId={activity.ParentSpanId} spanId={activity.SpanId} " +
            $"duration={activity.Duration.TotalMilliseconds:F1}ms tags=[{tags}]");
    }
};

ActivitySource.AddActivityListener(listener);

var app = builder.Build();

app.MapPost("/stores", (CreateStoreRequest request, ConcurrentDictionary<Guid, Store> stores) =>
{
    var store = new Store(
        Guid.NewGuid(),
        request.Name,
        request.Address,
        Latitude: null,
        Longitude: null,
        DateTimeOffset.UtcNow);

    stores[store.Id] = store;

    return Results.Created($"/stores/{store.Id}", store);
});

app.MapGet("/stores/{id:guid}", (Guid id, ConcurrentDictionary<Guid, Store> stores) =>
    stores.TryGetValue(id, out var store)
        ? Results.Ok(store)
        : Results.NotFound());

app.MapGet("/stores", (ConcurrentDictionary<Guid, Store> stores) => stores.Values);

app.Run();

record Store(
    Guid Id,
    string Name,
    string Address,
    double? Latitude,
    double? Longitude,
    DateTimeOffset CreatedAt);

record CreateStoreRequest(string Name, string Address);
