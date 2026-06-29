using Core;

namespace MedClinic.Api;

/// <summary>
/// Interim ITenantContext backed by the request's resolved tenant id.
/// TODO: replace with Finbuckle's IMultiTenantContextAccessor once the
/// multitenancy middleware is wired up at the host level.
/// </summary>
public sealed class HttpTenantContext(IHttpContextAccessor httpContextAccessor) : ITenantContext
{
    public Guid TenantId
    {
        get
        {
            var header = httpContextAccessor.HttpContext?.Request.Headers["X-Tenant-Id"].ToString();
            if (!Guid.TryParse(header, out var tenantId))
            {
                throw new InvalidOperationException("No tenant could be resolved for the current request.");
            }

            return tenantId;
        }
    }
}
