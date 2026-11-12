using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

/// <summary>
/// Turns unhandled exceptions into ProblemDetails responses.
/// <see href="https://learn.microsoft.com/aspnet/core/fundamentals/error-handling?view=aspnetcore-10.0#iexceptionhandler"/>
/// </summary>
/// <param name="logger">
/// Logs the exception, which .NET 10 no longer does once this handler returns true.
/// <see href="https://learn.microsoft.com/aspnet/core/breaking-changes/10/exception-handler-diagnostics-suppressed?view=aspnetcore-10.0"/>
/// <see href="https://opentelemetry.io/docs/specs/semconv/exceptions/exceptions-logs/"/>
/// </param>
internal sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    /// <summary>
    /// Set here because the ASP.NET Core instrumentation never sees exceptions this handler catches.
    /// <see href="https://opentelemetry.io/docs/specs/semconv/http/http-spans/"/>
    /// </summary>
    private const string ErrorTypeTag = "error.type";

    /// <remarks>The traceparent header is written again because UseExceptionHandler cleared the response.</remarks>
    /// <returns>Always true, even without a body: false makes the middleware log the exception again.</returns>
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var activity = Activity.Current;
        activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
        activity?.SetTag(ErrorTypeTag, exception.GetType().FullName);
        logger.LogError(exception, "Request failed");

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

        if (activity is not null)
        {
            httpContext.Response.Headers.TraceParent = activity.Id;
        }

        await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
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

        return true;
    }
}
