using Core;
using Finbuckle.MultiTenant.AspNetCore.Extensions;

namespace MedClinic.Api;

/// <summary>
/// Bridges Finbuckle's resolved ClinicTenantInfo into the ITenantContext used by module DbContexts.
/// Falls back to BackgroundJobTenantScope for Hangfire jobs that run without an HTTP request.
/// </summary>
public sealed class FinbuckleTenantContext(IHttpContextAccessor httpContextAccessor) : ITenantContext
{
    public Guid TenantId
    {
        get
        {
            if (BackgroundJobTenantScope.IsActive)
                return BackgroundJobTenantScope.Current;

            var info = httpContextAccessor.HttpContext?
                .GetMultiTenantContext<ClinicTenantInfo>()?.TenantInfo;

            if (info is null || !Guid.TryParse(info.Identifier, out var tenantId))
                throw new InvalidOperationException(
                    "No tenant could be resolved for the current request.");

            return tenantId;
        }
    }
}
