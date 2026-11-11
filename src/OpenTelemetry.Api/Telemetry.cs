using System.Diagnostics;

/// <summary>
/// The app's own ActivitySource, created once and shared for the process lifetime.
/// <see href="https://learn.microsoft.com/dotnet/core/diagnostics/distributed-tracing-instrumentation-walkthroughs#add-basic-instrumentation"/>
/// </summary>
public static class Telemetry
{
    /// <summary>Named after the assembly; shared by the code that starts activities and by AddSource in ConfigureTracing.</summary>
    public const string SourceName = "StoreApi";

    private const string SourceVersion = "1.0.0";

    public static readonly ActivitySource Source = new(SourceName, SourceVersion);
}
