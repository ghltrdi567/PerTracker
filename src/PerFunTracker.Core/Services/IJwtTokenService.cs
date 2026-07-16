namespace PerFunTracker.Core.Services;

/// <summary>
/// Generates JWT access tokens and refresh tokens, and validates refresh tokens.
/// </summary>
public interface IJwtTokenService
{
    /// <summary>Generates a short-lived JWT access token for the user.</summary>
    string GenerateAccessToken(string userId, string email, string displayName);

    /// <summary>Generates a random opaque refresh token value.</summary>
    string GenerateRefreshToken();
}
