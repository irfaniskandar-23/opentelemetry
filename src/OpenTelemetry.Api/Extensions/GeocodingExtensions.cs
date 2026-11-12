using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

public static class GeocodingExtensions
{
    public const string ClientName = "Geocoding";

    /// <summary>
    /// Registers the named HttpClient that calls the Nominatim geocoding API.
    /// <see href="https://learn.microsoft.com/dotnet/core/extensions/httpclient-factory#named-clients"/>
    /// </summary>
    public static IServiceCollection AddGeocoding(this IServiceCollection services)
    {
        services.AddHttpClient(ClientName, (serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<GeocodingOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
        });
        return services;
    }
}
