namespace Microsoft.Extensions.DependencyInjection;

public static class ConfigurationExtensions
{
    /// <summary>Registers every options class, validated at startup.</summary>
    public static IServiceCollection AddConfiguration(this IServiceCollection services)
    {
        services.AddValidatedOptions<BetterStackOptions>(BetterStackOptions.SectionName);
        services.AddValidatedOptions<GeocodingOptions>(GeocodingOptions.SectionName);
        return services;
    }
}
