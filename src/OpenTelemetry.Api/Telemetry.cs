using System.Diagnostics;

// The application's own ActivitySource. ASP.NET Core has one; this is ours.
//
// Static and shared on purpose. An ActivitySource is meant to exist once per
// component for the lifetime of the process — it is not a per-request object,
// and it does not belong in the DI container.
public static class Telemetry
{
    // Named separately because two places need it: the code that starts
    // activities, and the listener that decides whether to allow them.
    public const string SourceName = "OpenTelemetry.Api";

    public static readonly ActivitySource Source = new(SourceName, "1.0.0");
}
