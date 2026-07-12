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
    public async ValueTask<bool> TryHandleAsync(
        HttpContext ctx,
        Exception exception,
        CancellationToken cancellationToken)
    {
        // Log the exception type (never the message — may contain PHI or secrets).
        logger.LogError(
            "Unhandled {ExceptionType} on {Method} {Path}",
            exception.GetType().Name,
            ctx.Request.Method,
            ctx.Request.Path);

        var problem = new ProblemDetails
        {
            Type     = "https://tools.ietf.org/html/rfc9110#section-15.6.1",
            Title    = "An unexpected error occurred",
            Status   = StatusCodes.Status500InternalServerError,
            Detail   = "The server encountered an error processing your request.",
            Instance = ctx.Request.Path,
        };

        problem.Extensions["traceId"] = ctx.TraceIdentifier;

        ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await ctx.Response.WriteAsJsonAsync(problem, cancellationToken).ConfigureAwait(false);
        return true;
    }
}
