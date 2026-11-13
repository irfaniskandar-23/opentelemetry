using System.Diagnostics;

namespace Microsoft.AspNetCore.Builder;

public static class TraceParentExtensions
{
    /// <summary>
    /// Stamps the trace on every response for bug reports; set before next(), as headers go with the first byte.
    /// <see href="https://www.w3.org/TR/trace-context/#traceparent-header"/>
    /// </summary>
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
