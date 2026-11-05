using Microsoft.Extensions.Options;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.DependencyInjection;

public static class ObservabilityExtensions
{
    private const string TracesExporterName = "traces";
    private const string LogsExporterName = "logs";

    /// <summary>Sends traces and logs to Better Stack over OTLP, tagged with service, environment and host.</summary>
    public static IServiceCollection AddObservability(
        this IServiceCollection services, IHostEnvironment environment)
    {
        ConfigureExporter(services, TracesExporterName, "v1/traces");
        ConfigureExporter(services, LogsExporterName, "v1/logs");

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService("store-api")
                .AddAttributes([
                    new("deployment.environment.name", environment.EnvironmentName),
                    new("host.name", Environment.MachineName)]))
            .WithTracing(tracing => tracing
                .AddSource(Telemetry.SourceName)
                .AddAspNetCoreInstrumentation(options =>
                {
                    options.EnrichWithHttpRequest = (activity, request) =>
                    {
                        activity.SetTag("http.request.body.size", request.ContentLength);
                        activity.SetTag("http.request.header.content_type", request.ContentType);
                        activity.SetTag("url.query", request.QueryString.Value);
                        activity.SetTag("client.address", request.HttpContext.Connection.RemoteIpAddress?.ToString());
                    };
                })
                .AddHttpClientInstrumentation(options =>
                {
                    options.EnrichWithHttpRequestMessage = (activity, _) =>
                        activity.SetTag("http.client.name", GeocodingExtensions.ClientName);
                })
                .AddOtlpExporter(TracesExporterName, configure: null))
            .WithLogging(logging => logging
                .AddOtlpExporter(LogsExporterName, configureExporter: null));

        return services;
    }

    /// <summary>
    /// Fills one signal's named OTLP options from Better Stack settings.
    /// <see href="https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/src/OpenTelemetry.Exporter.OpenTelemetryProtocol/README.md"/>
    /// </summary>
    private static void ConfigureExporter(IServiceCollection services, string name, string path)
        => services.AddOptions<OtlpExporterOptions>(name)
            .Configure<IOptions<BetterStackOptions>>((exporter, betterStack) =>
            {
                exporter.Endpoint = new Uri($"{betterStack.Value.Endpoint}/{path}");
                exporter.Protocol = OtlpExportProtocol.HttpProtobuf;
                exporter.Headers = $"Authorization=Bearer {betterStack.Value.SourceToken}";
            });
}
