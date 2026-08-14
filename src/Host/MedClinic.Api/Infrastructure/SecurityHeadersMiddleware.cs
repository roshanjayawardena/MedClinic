namespace MedClinic.Api.Infrastructure;

/// <summary>
/// Adds security response headers required by standard audits (OWASP, HIPAA-adjacent guidance).
///
/// Pipeline position: immediately after exception handling, before any response-producing middleware,
/// so every response — including error responses — carries these headers.
///
/// CSP is intentionally restrictive for a JSON API: no content is ever rendered in a browser
/// context, so default-src 'none' is correct. frame-ancestors 'none' supersedes X-Frame-Options
/// in modern browsers; both are set for compatibility.
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    // Single-tenant Scalar/OpenAPI docs need script-src 'self' 'unsafe-inline' and style-src 'self'
    // 'unsafe-inline' in development — those paths are excluded from the strict API policy below.
    private static readonly PathString ScalarPrefix = new("/scalar");
    private static readonly PathString OpenApiPrefix = new("/openapi");
    private static readonly PathString HangfirePrefix = new("/hangfire");

    public async Task InvokeAsync(HttpContext context, IWebHostEnvironment env)
    {
        var headers = context.Response.Headers;

        // --- Headers that apply to every response --------------------------------
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
        headers["X-Permitted-Cross-Domain-Policies"] = "none";

        // HSTS: only meaningful over HTTPS and not in development (no valid cert on localhost).
        if (!env.IsDevelopment())
            headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";

        // --- CSP: relax for developer-only UI paths in Development ---------------
        var path = context.Request.Path;
        var isDeveloperUi =
            env.IsDevelopment() &&
            (path.StartsWithSegments(ScalarPrefix) ||
             path.StartsWithSegments(OpenApiPrefix) ||
             path.StartsWithSegments(HangfirePrefix));

        headers["Content-Security-Policy"] = isDeveloperUi
            ? "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; frame-ancestors 'none'"
            : "default-src 'none'; frame-ancestors 'none'";

        await next(context);
    }
}
