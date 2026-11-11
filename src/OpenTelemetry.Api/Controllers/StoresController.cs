using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;

/// <summary>Creates and reads stores; the geocoding call and errors are traced.</summary>
[ApiController]
[Route("stores")]
public sealed class StoresController(
    ConcurrentDictionary<Guid, Store> stores,
    IHttpClientFactory httpClientFactory,
    ILogger<StoresController> logger) : ControllerBase
{
    private const string CreateStoreSpanName = "CreateStore";

    private const string StoreIdTag = "store.id";
    private const string StoreNameTag = "store.name";
    private const string StoreAddressTag = "store.address";
    private const string GeocodeMatchCountTag = "geocode.match_count";
    private const string GeocodeLatitudeTag = "geocode.latitude";
    private const string GeocodeLongitudeTag = "geocode.longitude";
    private const string ErrorTypeTag = "error.type";

    /// <remarks>
    /// Geocoding runs inside the CreateStore activity so its client span nests; GlobalExceptionHandler records the exception once.
    /// <see href="https://opentelemetry.io/docs/specs/semconv/general/recording-errors/"/>
    /// </remarks>
    [HttpPost]
    public async Task<IActionResult> Create(
        CreateStoreRequest request,
        CancellationToken cancellationToken)
    {
        using var activity = Telemetry.Source.StartActivity(CreateStoreSpanName);

        activity?.SetTag(StoreNameTag, request.Name);
        activity?.SetTag(StoreAddressTag, request.Address);

        try
        {
            var geocodingClient = httpClientFactory.CreateClient(GeocodingExtensions.ClientName);

            var response = await geocodingClient.GetAsync(
                $"search?q={Uri.EscapeDataString(request.Address)}&format=json&limit=1",
                cancellationToken);

            response.EnsureSuccessStatusCode();

            var matches = await response.Content
                .ReadFromJsonAsync<GeocodingMatch[]>(cancellationToken) ?? [];

            activity?.SetTag(GeocodeMatchCountTag, matches.Length);

            if (matches.Length == 0)
            {
                throw new InvalidOperationException(
                    $"No coordinates found for address '{request.Address}'.");
            }

            var latitude = double.Parse(matches[0].Lat, CultureInfo.InvariantCulture);
            var longitude = double.Parse(matches[0].Lon, CultureInfo.InvariantCulture);

            var store = new Store(
                Guid.NewGuid(),
                request.Name,
                request.Address,
                latitude,
                longitude,
                DateTimeOffset.UtcNow);

            activity?.SetTag(StoreIdTag, store.Id);

            activity?.SetTag(GeocodeLatitudeTag, latitude);
            activity?.SetTag(GeocodeLongitudeTag, longitude);

            stores[store.Id] = store;

            return Created($"/stores/{store.Id}", store);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.SetTag(ErrorTypeTag, ex.GetType().FullName);

            throw;
        }
    }

    [HttpGet("{id:guid}")]
    public IActionResult GetById(Guid id)
    {
        Activity.Current?.SetTag(StoreIdTag, id);

        if (stores.TryGetValue(id, out var store))
        {
            logger.LogInformation("Store found");
            return Ok(store);
        }

        logger.LogWarning("Store not found");
        return NotFound();
    }

    [HttpGet]
    public IActionResult List()
    {
        return Ok(stores.Values);
    }

    [HttpGet("{id:guid}/boom")]
    public IActionResult Boom(Guid id)
    {
        throw new InvalidOperationException($"Deliberate failure for store {id}.");
    }
}
