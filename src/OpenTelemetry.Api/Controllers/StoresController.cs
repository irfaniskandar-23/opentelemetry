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
    /// <remarks>
    /// Geocoding runs inside the CreateStore activity so its client span nests; GlobalExceptionHandler records the exception once.
    /// <see href="https://opentelemetry.io/docs/specs/semconv/general/recording-errors/"/>
    /// </remarks>
    [HttpPost]
    public async Task<IActionResult> Create(
        CreateStoreRequest request,
        CancellationToken cancellationToken)
    {
        using var activity = Telemetry.Source.StartActivity("CreateStore");

        activity?.SetTag("store.name", request.Name);
        activity?.SetTag("store.address", request.Address);

        try
        {
            var geocodingClient = httpClientFactory.CreateClient(GeocodingExtensions.ClientName);

            var response = await geocodingClient.GetAsync(
                $"search?q={Uri.EscapeDataString(request.Address)}&format=json&limit=1",
                cancellationToken);

            response.EnsureSuccessStatusCode();

            var matches = await response.Content
                .ReadFromJsonAsync<GeocodingMatch[]>(cancellationToken) ?? [];

            activity?.SetTag("geocode.match_count", matches.Length);

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

            activity?.SetTag("store.id", store.Id);

            activity?.SetTag("geocode.latitude", latitude);
            activity?.SetTag("geocode.longitude", longitude);

            stores[store.Id] = store;

            return Created($"/stores/{store.Id}", store);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.SetTag("error.type", ex.GetType().FullName);

            throw;
        }
    }

    [HttpGet("{id:guid}")]
    public IActionResult GetById(Guid id)
    {
        Activity.Current?.SetTag("store.id", id);

        if (stores.TryGetValue(id, out var store))
        {
            logger.LogInformation("Store found");
            return Ok(store);
        }

        logger.LogWarning("Store not found");
        return NotFound();
    }

    [HttpGet]
    public IActionResult List() => Ok(stores.Values);

    [HttpGet("{id:guid}/boom")]
    public IActionResult Boom(Guid id)
    {
        throw new InvalidOperationException($"Deliberate failure for store {id}.");
    }
}
