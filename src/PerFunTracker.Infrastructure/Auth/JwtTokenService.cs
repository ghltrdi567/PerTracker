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

    /// <summary>
    /// Генерирует JWT access token — короткоживущий (15 мин) токен авторизации.
    ///
    /// Алгоритм:
    /// 1. Берём 256-битный HMAC-SHA256 ключ из конфига (должен быть ≥ 32 байт).
    /// 2. Собираем claims (утверждения о пользователе):
    ///    - sub (subject) — ID пользователя (уникальный идентификатор субъекта токена)
    ///    - email — для отображения и поиска
    ///    - display_name — кастомный claim, читается фронтендом
    ///    - jti (JWT ID) — уникальный GUID токена (защита от replay-атак: если токен
    ///      украден, его можно заблокировать по jti на стороне сервера)
    ///    - is_admin — "true" если админ (проверяется политикой "Admin" в Program.cs)
    /// 3. Подписываем ключом (HMAC-SHA256), задаём expires = UtcNow + AccessTokenMinutes.
    /// 4. Сериализуем в компактный формат: header.payload.signature (base64, разделён точками).
    ///
    /// Почему HMAC-SHA256 (симметричный), а не RSA/ECDSA (асимметричный):
    /// - В монолите API сам и выдаёт, и проверяет токены — симметричного ключа достаточно.
    /// - Асимметричное подписание нужно в микросервисах, где один сервис выпускает токены,
    ///   а другие только проверяют подпись публичным ключом.
    /// </summary>
    public string GenerateAccessToken(string userId, string email, string displayName, bool isAdmin = false)
    {
        // Ключ подписи — симметричный. Convert.FromBase64String декодирует строку из конфига.
        var key = new SymmetricSecurityKey(Convert.FromBase64String(_options.Key));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        // Claims — это утверждения о пользователе, закодированные в токен.
        // Любой сервис может прочитать их без обращения к БД (токен самодостаточен).
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId),
            new(JwtRegisteredClaimNames.Email, email),
            new("display_name", displayName),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        // is_admin claim добавляется только админам.
        // Политика "Admin" в Program.cs проверяет: RequireClaim("is_admin", "true").
        // У обычных пользователей этого claim нет → 403 Forbidden.
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

    /// <summary>
    /// Генерирует opaque refresh token (непрозрачный, в отличие от JWT).
    ///
    /// Отличие от access token:
    /// - Access token — это JWT (содержит данные пользователя, подписан). Живёт 15 минут.
    /// - Refresh token — это просто случайная строка. Не содержит данных. Хранится в БД. Живёт 7 дней.
    ///
    /// Почему refresh token — не JWT, а случайная строка:
    /// - Если refresh token — JWT, его нельзя отозвать до истечения срока (JWT stateless).
    /// - Если refresh token — строка в БД, мы ставим RevokedAt = now и он сразу недействителен.
    /// - Это позволяет реализовать ротацию: при каждом refresh старый токен ревокается,
    ///   выдаётся новый. Повторное использование старого → 401 Unauthorized.
    ///
    /// CSPRNG vs Random:
    /// - RandomNumberGenerator.GetBytes (CSPRNG) — криптостойкий генератор, непредсказуем.
    /// - Random.NextBytes — НЕ криптостойкий, предсказуем (можно вычислить следующий токен).
    /// Для refresh-токена (аналог пароля) нужен ТОЛЬКО CSPRNG.
    ///
    /// 32 байта = 256 бит случайности → base64 строка ~44 символа.
    /// Перебор 2^256 вариантов невозможен даже на квантовом компьютере.
    /// </summary>
    public string GenerateRefreshToken()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    }
}
