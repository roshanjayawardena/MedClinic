using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Web;

/// <summary>
/// Catches unhandled exceptions and returns RFC 9457 ProblemDetails.
/// Never leaks stack traces or exception messages to callers.
///
/// Architecture note: expected domain failures (not found, conflict, validation)
/// must be handled via Result&lt;T&gt; in handlers and never reach this handler.
/// Only true infrastructure failures should land here.
/// </summary>
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    : IExceptionHandler
{
    private static readonly Action<ILogger, string, string, string, Exception?> LogUnhandled =
        LoggerMessage.Define<string, string, string>(
            LogLevel.Error,
            new EventId(1, "UnhandledException"),
            "Unhandled {ExceptionType} on {Method} {Path}");

    private static readonly Action<ILogger, string, string, Exception?> LogConcurrency =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            new EventId(2, "ConcurrencyConflict"),
            "Concurrency conflict on {Method} {Path}");

    private static readonly Action<ILogger, string, string, Exception?> LogCancelled =
        LoggerMessage.Define<string, string>(
            LogLevel.Information,
            new EventId(3, "RequestCancelled"),
            "Request cancelled on {Method} {Path}");

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        // Client cancelled — connection is gone, nothing to write, not a server error.
        if (exception is OperationCanceledException)
        {
            LogCancelled(logger, httpContext.Request.Method, httpContext.Request.Path, null);
            return true;
        }

        int status;
        string title;
        string detail;

        // Match by type name — keeps Web building block free of EF Core dependency.
        if (exception.GetType().Name == "DbUpdateConcurrencyException")
        {
            // Two requests modified the same row simultaneously.
            LogConcurrency(logger, httpContext.Request.Method, httpContext.Request.Path, null);
            status = StatusCodes.Status409Conflict;
            title  = "Conflict";
            detail = "The resource was modified by another request. Please retry.";
        }
        else
        {
            // True infrastructure failure — log type only, never the message
            // (exception messages may contain connection strings, PHI, or secrets).
            LogUnhandled(logger, exception.GetType().Name,
                         httpContext.Request.Method, httpContext.Request.Path, null);
            status = StatusCodes.Status500InternalServerError;
            title  = "An unexpected error occurred";
            detail = "The server encountered an error processing your request.";
        }

        var problem = new ProblemDetails
        {
            Type     = "https://tools.ietf.org/html/rfc9110#section-15.6.1",
            Title    = title,
            Status   = status,
            Detail   = detail,
            Instance = httpContext.Request.Path,
        };
        problem.Extensions["traceId"] = httpContext.TraceIdentifier;

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken).ConfigureAwait(false);
        return true;
    }
}
