using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

/// <summary>
/// Turns unhandled exceptions into ProblemDetails responses.
/// <see href="https://learn.microsoft.com/aspnet/core/fundamentals/error-handling?view=aspnetcore-10.0#iexceptionhandler"/>
/// </summary>
internal sealed class GlobalExceptionHandler(IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    /// <remarks>The traceparent header is written again because UseExceptionHandler cleared the response.</remarks>
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var activity = Activity.Current;
        activity?.SetStatus(ActivityStatusCode.Error, exception.Message);

        // The label on the failed span: same value for every failure of this
        // kind, so failures can be grouped and counted. The message above is
        // unique per failure; this is not. Required by the HTTP conventions when
        // a request ends in error, and nobody else sets it here — the ASP.NET
        // Core instrumentation's exception hooks never see an exception that
        // UseExceptionHandler has already caught.
        // https://opentelemetry.io/docs/specs/semconv/http/http-spans/
        activity?.SetTag("error.type", exception.GetType().FullName);

        activity?.AddException(exception);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

        if (activity is not null)
        {
            httpContext.Response.Headers.TraceParent = activity.Id;
        }

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected error occurred.",
                Detail = "The request could not be completed."
            }
        });
    }
}
