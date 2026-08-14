using Clinics.Persistence;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace MedClinic.Api;

/// <summary>
/// Finbuckle tenant store backed by the clinics database.
/// Replaces PassthroughTenantStore — every request now validates against a real Clinic record.
/// An inactive or unknown clinic GUID is rejected (returns null → 404 from Finbuckle).
/// </summary>
public sealed class DatabaseClinicStore(IDbContextFactory<ClinicsDbContext> dbFactory)
    : IMultiTenantStore<ClinicTenantInfo>
{
    // Finbuckle calls GetByIdentifierAsync with the raw header value (the GUID string).
    public async Task<ClinicTenantInfo?> GetByIdentifierAsync(string identifier)
    {
        if (!Guid.TryParse(identifier, out var id))
            return null;

        return await GetAsync(id.ToString()).ConfigureAwait(false);
    }

    public async Task<ClinicTenantInfo?> GetAsync(string id)
    {
        if (!Guid.TryParse(id, out var clinicId))
            return null;

        await using var db = await dbFactory.CreateDbContextAsync().ConfigureAwait(false);

        var clinic = await db.Clinics
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == clinicId && c.IsActive)
            .ConfigureAwait(false);

        return clinic is null ? null : new ClinicTenantInfo
        {
            Id           = clinic.Id.ToString(),
            Identifier   = clinic.Id.ToString(),
            Name         = clinic.Name,
            ContactEmail = clinic.ContactEmail,
            TimeZoneId   = clinic.TimeZoneId,
        };
    }

    // Used by DailyDigestJob to enumerate all active clinics.
    public async Task<IEnumerable<ClinicTenantInfo>> GetAllAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync().ConfigureAwait(false);

        return await db.Clinics
            .AsNoTracking()
            .Where(c => c.IsActive)
            .Select(c => new ClinicTenantInfo
            {
                Id           = c.Id.ToString(),
                Identifier   = c.Id.ToString(),
                Name         = c.Name,
                ContactEmail = c.ContactEmail,
                TimeZoneId   = c.TimeZoneId,
            })
            .ToListAsync()
            .ConfigureAwait(false);
    }

    public Task<IEnumerable<ClinicTenantInfo>> GetAllAsync(int take, int skip) =>
        throw new NotSupportedException("Paged GetAll is not used in this implementation.");

    public Task<bool> AddAsync(ClinicTenantInfo tenantInfo)    => Task.FromResult(false);
    public Task<bool> UpdateAsync(ClinicTenantInfo tenantInfo) => Task.FromResult(false);
    public Task<bool> RemoveAsync(string identifier)           => Task.FromResult(false);
}
