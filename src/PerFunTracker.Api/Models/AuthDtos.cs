namespace PerFunTracker.Api.Models;

/// <summary>Request body for POST /api/auth/register</summary>
public record RegisterRequest(string Email, string Password, string DisplayName);

/// <summary>Request body for POST /api/auth/login</summary>
public record LoginRequest(string Email, string Password);

/// <summary>Response body for login and refresh — access token + expiry.</summary>
public record TokenResponse(string AccessToken, int ExpiresIn);

/// <summary>Generic error response.</summary>
public record ErrorResponse(string[] Errors);
