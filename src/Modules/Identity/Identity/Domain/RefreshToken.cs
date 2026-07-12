namespace Identity.Domain;

public sealed class RefreshToken
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid UserId { get; private set; }
    public Guid ClinicId { get; private set; }
    public string Token { get; private set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public bool IsRevoked { get; private set; }
    public string? ReplacedByToken { get; private set; }

    private RefreshToken() { }

    public static RefreshToken Create(Guid userId, Guid clinicId, TimeSpan lifetime, TimeProvider clock)
    {
        return new RefreshToken
        {
            UserId    = userId,
            ClinicId  = clinicId,
            Token     = GenerateToken(),
            ExpiresAt = clock.GetUtcNow().Add(lifetime),
            CreatedAt = clock.GetUtcNow(),
        };
    }

    public bool IsExpired(TimeProvider clock) => clock.GetUtcNow() >= ExpiresAt;
    public bool IsActive(TimeProvider clock) => !IsRevoked && !IsExpired(clock);

    public void Revoke(string? replacedBy = null)
    {
        IsRevoked       = true;
        ReplacedByToken = replacedBy;
    }

    private static string GenerateToken()
    {
        var bytes = new byte[64];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }
}
