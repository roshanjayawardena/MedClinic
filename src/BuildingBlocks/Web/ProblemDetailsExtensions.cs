using Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Web;

/// <summary>
/// Maps Result&lt;T&gt; failures to RFC 9457 ProblemDetails responses.
/// </summary>
public static class ProblemDetailsExtensions
{
    public static IResult ToProblemResult(this Error error, HttpContext ctx)
    {
        var (status, type) = MapErrorCode(error.Code);

        var problem = new ProblemDetails
        {
            Type   = type,
            Title  = TitleFor(status),
            Status = status,
            Detail = error.Message,
            Instance = ctx.Request.Path,
        };

        problem.Extensions["traceId"]   = ctx.TraceIdentifier;
        problem.Extensions["errorCode"] = error.Code;

        return TypedResults.Problem(problem);
    }

    private static (int status, string type) MapErrorCode(string code) => code switch
    {
        var c when c.EndsWith(".NotFound")          => (404, "https://tools.ietf.org/html/rfc9110#section-15.5.5"),
        var c when c.EndsWith(".Conflict")          => (409, "https://tools.ietf.org/html/rfc9110#section-15.5.10"),
        var c when c.EndsWith(".Forbidden")         => (403, "https://tools.ietf.org/html/rfc9110#section-15.5.4"),
        var c when c.EndsWith(".Unauthorized")      => (401, "https://tools.ietf.org/html/rfc9110#section-15.5.2"),
        var c when c.EndsWith(".Unprocessable")     => (422, "https://tools.ietf.org/html/rfc9110#section-15.5.21"),
        var c when c.StartsWith("Auth.")            => (401, "https://tools.ietf.org/html/rfc9110#section-15.5.2"),
        var c when c.EndsWith(".InvalidCredentials")=> (401, "https://tools.ietf.org/html/rfc9110#section-15.5.2"),
        _                                           => (400, "https://tools.ietf.org/html/rfc9110#section-15.5.1"),
    };

    private static string TitleFor(int status) => status switch
    {
        400 => "Bad Request",
        401 => "Unauthorized",
        403 => "Forbidden",
        404 => "Not Found",
        409 => "Conflict",
        422 => "Unprocessable Entity",
        _   => "An error occurred",
    };
}
