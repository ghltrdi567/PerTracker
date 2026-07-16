namespace PerFunTracker.Core.Entities;

/// <summary>
/// A refresh token issued to a user for obtaining new JWT access tokens.
/// Stored in the database so we can revoke/rotate tokens.
/// </summary>
public class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The user this token belongs to.</summary>
    public string UserId { get; set; } = string.Empty;
    public AppUser? User { get; set; }

    /// <summary>Opaque random token value sent to the client as an httpOnly cookie.</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>When the token expires.</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>When the token was revoked (null = active).</summary>
    public DateTime? RevokedAt { get; set; }

    /// <summary>Token that replaced this one during rotation (null = not rotated).</summary>
    public Guid? ReplacedByTokenId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
    public bool IsRevoked => RevokedAt != null;
    public bool IsActive => !IsExpired && !IsRevoked;
}
