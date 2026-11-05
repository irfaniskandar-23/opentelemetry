using System.Diagnostics;

namespace Microsoft.AspNetCore.Builder;

public static class TraceParentExtensions
{
    /// <summary>
    /// Stamps the current trace onto every response so a caller can quote it in a bug report.
    /// <see href="https://www.w3.org/TR/trace-context/#traceparent-header"/>
    /// </summary>
    /// <remarks>Set before next(): headers are already sent once the endpoint writes its first byte.</remarks>
    public static IApplicationBuilder UseTraceParentHeader(this IApplicationBuilder app)
    {
        app.Use(async (context, next) =>
        {
            var activity = Activity.Current;

            if (activity is not null)
            {
                context.Response.Headers.TraceParent = activity.Id;
            }

            await next();
        });
        return app;
    }
}
