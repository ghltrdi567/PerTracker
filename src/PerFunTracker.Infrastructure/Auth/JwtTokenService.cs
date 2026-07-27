using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PerFunTracker.Core.Options;
using PerFunTracker.Core.Services;

namespace PerFunTracker.Infrastructure.Auth;

/// <summary>
/// Generates JWT access tokens and opaque refresh tokens.
/// </summary>
public class JwtTokenService : IJwtTokenService
{
    private readonly JwtOptions _options;

    public JwtTokenService(IOptions<JwtOptions> options)
    {
        _options = options.Value;
    }

    public string GenerateAccessToken(string userId, string email, string displayName, bool isAdmin = false)
    {
        var key = new SymmetricSecurityKey(Convert.FromBase64String(_options.Key));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId),
            new(JwtRegisteredClaimNames.Email, email),
            new("display_name", displayName),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        if (isAdmin)
        {
            claims.Add(new Claim("is_admin", "true"));
        }

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_options.AccessTokenMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string GenerateRefreshToken()
    {
        // 32 bytes of randomness → URL-safe base64
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    }
}
