using Finbuckle.MultiTenant.Abstractions;

namespace MedClinic.Api;

/// <summary>
/// Accepts any syntactically valid GUID as a tenant identifier.
/// No database round-trip during header strategy resolution — keeps the door open
/// for a database-backed store once a Clinics table is introduced.
/// </summary>
public sealed class PassthroughTenantStore : IMultiTenantStore<ClinicTenantInfo>
{
    public Task<bool> AddAsync(ClinicTenantInfo tenantInfo) => Task.FromResult(false);
    public Task<bool> UpdateAsync(ClinicTenantInfo tenantInfo) => Task.FromResult(false);
    public Task<bool> RemoveAsync(string identifier) => Task.FromResult(false);

    public Task<ClinicTenantInfo?> GetAsync(string id) =>
        Task.FromResult(Resolve(id));

    public Task<ClinicTenantInfo?> GetByIdentifierAsync(string identifier) =>
        Task.FromResult(Resolve(identifier));

    public Task<IEnumerable<ClinicTenantInfo>> GetAllAsync() =>
        Task.FromResult(Enumerable.Empty<ClinicTenantInfo>());

    public Task<IEnumerable<ClinicTenantInfo>> GetAllAsync(int take, int skip) =>
        Task.FromResult(Enumerable.Empty<ClinicTenantInfo>());

    private static ClinicTenantInfo? Resolve(string value)
    {
        if (!Guid.TryParse(value, out _))
            return null;

        return new ClinicTenantInfo { Id = value, Identifier = value, Name = value };
    }
}
