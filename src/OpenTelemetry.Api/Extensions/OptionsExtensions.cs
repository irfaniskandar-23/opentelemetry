using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

public static class OptionsExtensions
{
    /// <summary>
    /// Binds a config section to options and fails startup if validation fails.
    /// <see href="https://learn.microsoft.com/dotnet/core/extensions/options-library-authors#configuration-section-path-parameter"/>
    /// </summary>
    public static OptionsBuilder<T> AddValidatedOptions<T>(
        this IServiceCollection services, string sectionName)
        where T : class
    {
        return services.AddOptions<T>()
            .BindConfiguration(sectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();
    }
}
