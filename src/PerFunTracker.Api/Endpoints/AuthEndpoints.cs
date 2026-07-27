using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PerFunTracker.Core.Entities;
using PerFunTracker.Core.Options;
using PerFunTracker.Core.Services;
using PerFunTracker.Infrastructure.Data;
using PerFunTracker.Api.Models;

namespace PerFunTracker.Api.Endpoints;

/// <summary>
/// Auth endpoint group: register, confirm-email, login, refresh, logout.
/// </summary>
public static class AuthEndpoints
{
    private const string RefreshCookieName = "refreshToken";

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").RequireRateLimiting("auth");

        group.MapPost("/register", Register);
        group.MapGet("/confirm-email", ConfirmEmail);
        group.MapPost("/login", Login);
        group.MapPost("/refresh", Refresh);
        group.MapPost("/logout", Logout);
        group.MapPost("/make-admin", MakeAdmin).RequireAuthorization("Admin");

        return app;
    }

    private static async Task<Results<Created, BadRequest<ErrorResponse>>> Register(
        RegisterRequest request,
        UserManager<AppUser> userManager,
        ApplicationDbContext db,
        IEmailSender emailSender,
        HttpContext ctx)
    {
        // Первый зарегистрированный пользователь становится админом
        var isFirstUser = !await db.Users.AnyAsync();

        var user = new AppUser
        {
            UserName = request.Email,
            Email = request.Email,
            DisplayName = request.DisplayName,
            IsAdmin = isFirstUser,
            CreatedAt = DateTime.UtcNow,
        };

        var result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
            return TypedResults.BadRequest(new ErrorResponse(result.Errors.Select(e => e.Description).ToArray()));

        var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
        var tokenEncoded = Uri.EscapeDataString(token);

        var scheme = ctx.Request.Scheme;
        var host = ctx.Request.Host;
        // Ссылка ведёт на фронтенд, который вызовет API и покажет результат
        var confirmUrl = $"{scheme}://{host}/confirm-email?userId={user.Id}&token={tokenEncoded}";

        var html = $"""
            <h2>Welcome to PerFunTracker!</h2>
            <p>Please confirm your email by clicking the link below:</p>
            <p><a href="{confirmUrl}">Confirm Email</a></p>
            <p style="color:#888;font-size:12px;">If you didn't create an account, you can ignore this email.</p>
            """;

        await emailSender.SendAsync(user.Email!, "Confirm your email — PerFunTracker", html);

        return TypedResults.Created();
    }

    private static async Task<Results<Ok<string>, BadRequest<string>>> ConfirmEmail(
        string userId,
        string token,
        UserManager<AppUser> userManager)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
            return TypedResults.BadRequest("Invalid user.");

        var result = await userManager.ConfirmEmailAsync(user, token);
        if (!result.Succeeded)
            return TypedResults.BadRequest("Invalid or expired token.");

        return TypedResults.Ok("Email confirmed successfully.");
    }

    private static async Task<Results<Ok<TokenResponse>, BadRequest<ErrorResponse>, UnauthorizedHttpResult>> Login(
        LoginRequest request,
        UserManager<AppUser> userManager,
        SignInManager<AppUser> signInManager,
        IJwtTokenService jwtTokenService,
        ApplicationDbContext db,
        IOptions<JwtOptions> jwtOptions,
        HttpContext ctx)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null)
            return TypedResults.Unauthorized();

        // CheckPasswordSignInAsync enforces lockout (Counts failed attempts, respects MaxFailedAccessAttempts)
        var signInResult = await signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (!signInResult.Succeeded)
            return TypedResults.Unauthorized();

        if (!await userManager.IsEmailConfirmedAsync(user))
            return TypedResults.BadRequest(new ErrorResponse(["Email not confirmed."]));

        var accessToken = jwtTokenService.GenerateAccessToken(user.Id, user.Email!, user.DisplayName, user.IsAdmin);
        var refreshToken = await IssueRefreshToken(user.Id, jwtTokenService, db, jwtOptions.Value);

        SetRefreshCookie(ctx, refreshToken, jwtOptions.Value.RefreshTokenDays);

        return TypedResults.Ok(new TokenResponse(accessToken, jwtOptions.Value.AccessTokenMinutes * 60));
    }

    private static async Task<Results<Ok<TokenResponse>, UnauthorizedHttpResult>> Refresh(
        HttpContext ctx,
        ApplicationDbContext db,
        IJwtTokenService jwtTokenService,
        UserManager<AppUser> userManager,
        IOptions<JwtOptions> jwtOptions)
    {
        if (!ctx.Request.Cookies.TryGetValue(RefreshCookieName, out var cookieToken) || string.IsNullOrEmpty(cookieToken))
            return TypedResults.Unauthorized();

        var stored = await db.RefreshTokens
            .FirstOrDefaultAsync(rt => rt.Token == cookieToken);

        if (stored is null || !stored.IsActive)
            return TypedResults.Unauthorized();

        // Revoke old token, issue a new one (rotation)
        stored.RevokedAt = DateTime.UtcNow;

        var user = await userManager.FindByIdAsync(stored.UserId);
        if (user is null)
            return TypedResults.Unauthorized();

        var newRefresh = await IssueRefreshToken(user.Id, jwtTokenService, db, jwtOptions.Value, stored.Id);
        SetRefreshCookie(ctx, newRefresh, jwtOptions.Value.RefreshTokenDays);

        var accessToken = jwtTokenService.GenerateAccessToken(user.Id, user.Email!, user.DisplayName, user.IsAdmin);
        return TypedResults.Ok(new TokenResponse(accessToken, jwtOptions.Value.AccessTokenMinutes * 60));
    }

    private static async Task<Ok> Logout(
        HttpContext ctx,
        ApplicationDbContext db)
    {
        if (ctx.Request.Cookies.TryGetValue(RefreshCookieName, out var cookieToken))
        {
            var stored = await db.RefreshTokens.FirstOrDefaultAsync(rt => rt.Token == cookieToken);
            if (stored is not null && stored.IsActive)
            {
                stored.RevokedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }
        }

        ctx.Response.Cookies.Delete(RefreshCookieName);
        return TypedResults.Ok();
    }

    // --- POST /api/auth/make-admin — promote a user to admin (admin only) ---

    private static async Task<Results<Ok, NotFound, BadRequest<ErrorResponse>>> MakeAdmin(
        MakeAdminRequest request,
        UserManager<AppUser> userManager)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null)
            return TypedResults.NotFound();

        if (user.IsAdmin)
            return TypedResults.BadRequest(new ErrorResponse(["User is already an admin."]));

        user.IsAdmin = true;
        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
            return TypedResults.BadRequest(new ErrorResponse(result.Errors.Select(e => e.Description).ToArray()));

        return TypedResults.Ok();
    }

    // --- Helpers ---

    private static async Task<string> IssueRefreshToken(
        string userId,
        IJwtTokenService jwtTokenService,
        ApplicationDbContext db,
        JwtOptions jwtOptions,
        Guid? replacedByTokenId = null)
    {
        var token = jwtTokenService.GenerateRefreshToken();
        var refresh = new RefreshToken
        {
            UserId = userId,
            Token = token,
            ExpiresAt = DateTime.UtcNow.AddDays(jwtOptions.RefreshTokenDays),
            ReplacedByTokenId = replacedByTokenId,
        };

        db.RefreshTokens.Add(refresh);
        await db.SaveChangesAsync();
        return token;
    }

    private static void SetRefreshCookie(HttpContext ctx, string token, int refreshTokenDays)
    {
        var isDev = ctx.RequestServices.GetRequiredService<IWebHostEnvironment>().IsDevelopment();

        ctx.Response.Cookies.Append(RefreshCookieName, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = !isDev,
            SameSite = isDev ? SameSiteMode.Lax : SameSiteMode.Strict,
            Path = "/api/auth",
            Expires = DateTimeOffset.UtcNow.AddDays(refreshTokenDays),
        });
    }
}
