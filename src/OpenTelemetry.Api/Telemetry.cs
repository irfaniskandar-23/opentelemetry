using System.Diagnostics;

/// <summary>The app's own ActivitySource, created once and shared for the process lifetime.</summary>
public static class Telemetry
{
    /// <summary>Shared by the code that starts activities and by AddSource in AddObservability.</summary>
    public const string SourceName = "OpenTelemetry.Api";

    public static readonly ActivitySource Source = new(SourceName, "1.0.0");
}
