using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Web;

/// <summary>
/// Catches unhandled exceptions and returns RFC 9457 ProblemDetails.
/// Never leaks stack traces or exception messages to callers.
/// </summary>
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    : IExceptionHandler
{
    // High-performance log delegate — avoids boxing and allocation on hot path.
    private static readonly Action<ILogger, string, string, string, Exception?> LogUnhandled =
        LoggerMessage.Define<string, string, string>(
            LogLevel.Error,
            new EventId(1, "UnhandledException"),
            "Unhandled {ExceptionType} on {Method} {Path}");

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        // Log the exception type (never the message — may contain PHI or secrets).
        LogUnhandled(logger, exception.GetType().Name, httpContext.Request.Method, httpContext.Request.Path, null);

        var problem = new ProblemDetails
        {
            Type     = "https://tools.ietf.org/html/rfc9110#section-15.6.1",
            Title    = "An unexpected error occurred",
            Status   = StatusCodes.Status500InternalServerError,
            Detail   = "The server encountered an error processing your request.",
            Instance = httpContext.Request.Path,
        };

        problem.Extensions["traceId"] = httpContext.TraceIdentifier;

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken).ConfigureAwait(false);
        return true;
    }
}
