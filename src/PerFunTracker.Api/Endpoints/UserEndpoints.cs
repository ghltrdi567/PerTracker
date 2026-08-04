using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using PerFunTracker.Api.Models;
using PerFunTracker.Core.Entities;
using PerFunTracker.Infrastructure.Data;

namespace PerFunTracker.Api.Endpoints;

/// <summary>
/// User settings endpoints (notification toggle).
/// </summary>
public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users").RequireAuthorization();

        group.MapGet("/me/settings", GetSettings);
        group.MapPut("/me/settings", UpdateSettings);

        return app;
    }

    private static async Task<Ok<UserSettingsResponse>> GetSettings(
        ApplicationDbContext db,
        HttpContext ctx)
    {
        var userId = GetUserId(ctx);

        var user = await db.Users.FindAsync(userId)
            ?? throw new InvalidOperationException("User not found.");

        return TypedResults.Ok(new UserSettingsResponse(user.NotifyByEmailOnCompletion));
    }

    private static async Task<Ok<UserSettingsResponse>> UpdateSettings(
        UserSettingsResponse request,
        ApplicationDbContext db,
        HttpContext ctx)
    {
        var userId = GetUserId(ctx);

        var user = await db.Users.FindAsync(userId)
            ?? throw new InvalidOperationException("User not found.");

        user.NotifyByEmailOnCompletion = request.NotifyByEmailOnCompletion;
        await db.SaveChangesAsync();

        return TypedResults.Ok(new UserSettingsResponse(user.NotifyByEmailOnCompletion));
    }

    /// <summary>
/// Извлекает ID пользователя из JWT claims.
///
    /// JWT содержит claim "sub" (subject) с ID пользователя.
/// Identity иногда использует ClaimTypes.NameIdentifier вместо sub.
/// Проверяем оба варианта для совместимости.
///
/// Если claim не найден — бросаем UnauthorizedAccessException.
/// Это не должно происходить при корректной авторизации (middleware отсечёт 401),
/// но защищает от багов в случае, если middleware пропустил запрос.
/// </summary>
private static string GetUserId(HttpContext ctx)
    {
        return ctx.User.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)
            ?? ctx.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedAccessException("User ID not found in token.");
    }
}
