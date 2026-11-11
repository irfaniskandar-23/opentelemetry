using System.Diagnostics;
using Microsoft.Extensions.Options;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.DependencyInjection;

public static class ObservabilityExtensions
{
    private const string ServiceName = "store-api";
    private const string TracesExporterName = "traces";
    private const string LogsExporterName = "logs";
    private const string TracesPath = "v1/traces";
    private const string LogsPath = "v1/logs";
    private const string DeploymentEnvironmentNameTag = "deployment.environment.name";
    private const string HostNameTag = "host.name";
    private const string HttpRequestBodySizeTag = "http.request.body.size";
    private const string HttpRequestHeaderContentTypeTag = "http.request.header.content_type";
    private const string UrlQueryTag = "url.query";
    private const string ClientAddressTag = "client.address";
    private const string HttpClientNameTag = "http.client.name";

    /// <summary>Sends traces and logs to Better Stack over OTLP, tagged with service, environment and host.</summary>
    public static IServiceCollection AddObservability(
        this IServiceCollection services, IHostEnvironment environment)
    {
        services.AddOpenTelemetry()
            .ConfigureResource(resource => DescribeService(resource, environment))
            .WithTracing(ConfigureTracing)
            .WithLogging(ConfigureLogging);

        SendToBetterStack(services, TracesExporterName, TracesPath);
        SendToBetterStack(services, LogsExporterName, LogsPath);

        return services;
    }

    private static void DescribeService(ResourceBuilder resource, IHostEnvironment environment)
    {
        resource
            .AddService(ServiceName)
            .AddAttributes([
                new(DeploymentEnvironmentNameTag, environment.EnvironmentName),
                new(HostNameTag, Environment.MachineName)]);
    }

    private static void ConfigureTracing(TracerProviderBuilder tracing)
    {
        tracing
            .AddSource(Telemetry.SourceName)
            .AddAspNetCoreInstrumentation(options => options.EnrichWithHttpRequest = TagRequestDetails)
            .AddHttpClientInstrumentation(options => options.EnrichWithHttpRequestMessage = TagClientName)
            .AddOtlpExporter(TracesExporterName, configure: null);
    }

    private static void TagRequestDetails(Activity activity, HttpRequest request)
    {
        activity.SetTag(HttpRequestBodySizeTag, request.ContentLength);
        activity.SetTag(HttpRequestHeaderContentTypeTag, request.ContentType);
        activity.SetTag(UrlQueryTag, request.QueryString.Value);
        activity.SetTag(ClientAddressTag, request.HttpContext.Connection.RemoteIpAddress?.ToString());
    }

    /// <summary>Labels every traced outgoing call as Geocoding, the only traced HttpClient today.</summary>
    private static void TagClientName(Activity activity, HttpRequestMessage _)
    {
        activity.SetTag(HttpClientNameTag, GeocodingExtensions.ClientName);
    }

    private static void ConfigureLogging(LoggerProviderBuilder logging)
    {
        logging.AddOtlpExporter(LogsExporterName, configureExporter: null);
    }

    /// <summary>
    /// Fills the named exporter's settings from Better Stack's, once those settings can be read.
    /// <see href="https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/src/OpenTelemetry.Exporter.OpenTelemetryProtocol/README.md"/>
    /// </summary>
    private static void SendToBetterStack(IServiceCollection services, string exporterName, string signalPath)
    {
        services.AddOptions<OtlpExporterOptions>(exporterName)
            .Configure<IOptions<BetterStackOptions>>(
                (exporter, betterStack) => PointAtBetterStack(exporter, betterStack.Value, signalPath));
    }

    /// <summary>The signal path is added by hand because an endpoint set in code is used as-is.</summary>
    /// <remarks>Its own HttpClient lets the last send work while a failed startup shuts the app down.</remarks>
    private static void PointAtBetterStack(OtlpExporterOptions exporter, BetterStackOptions betterStack, string signalPath)
    {
        exporter.Endpoint = new Uri($"{betterStack.Endpoint}/{signalPath}");
        exporter.Protocol = OtlpExportProtocol.HttpProtobuf;
        exporter.Headers = $"Authorization=Bearer {betterStack.SourceToken}";
        exporter.HttpClientFactory = () => new HttpClient { Timeout = TimeSpan.FromMilliseconds(exporter.TimeoutMilliseconds) };
    }
}
