using Microsoft.AspNetCore.Http;

namespace Identity.Middleware;

/// <summary>
/// Validates that the JWT clinic_id claim matches the X-Tenant-Id header on every authenticated request.
/// Without this check, a user from ClinicA holding a valid JWT could set X-Tenant-Id: ClinicB
/// and read or write data belonging to another clinic.
/// </summary>
public sealed class TenantClaimValidationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var claimedClinicId = context.User.FindFirst("clinic_id")?.Value;
            var headerClinicId = context.Request.Headers["X-Tenant-Id"].ToString();

            if (!string.IsNullOrEmpty(claimedClinicId)
                && !string.IsNullOrEmpty(headerClinicId)
                && !string.Equals(claimedClinicId, headerClinicId, StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "text/plain";
                await context.Response.WriteAsync(
                    "Forbidden: JWT clinic_id does not match X-Tenant-Id header.");
                return;
            }
        }

        await next(context);
    }
}
