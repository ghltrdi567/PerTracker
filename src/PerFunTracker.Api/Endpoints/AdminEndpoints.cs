using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using PerFunTracker.Api.Models;
using PerFunTracker.Core.Entities;
using PerFunTracker.Infrastructure.Data;

namespace PerFunTracker.Api.Endpoints;

/// <summary>
/// Admin dashboard endpoints — user management, event oversight.
/// All endpoints require the "Admin" policy.
/// </summary>
public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin").RequireAuthorization("Admin");

        group.MapGet("/users", ListUsers);
        group.MapDelete("/users/{userId}", DeleteUser);
        group.MapGet("/events", ListAllEvents);
        group.MapDelete("/events/{eventId:guid}", DeleteAnyEvent);

        return app;
    }

    // --- GET /api/admin/users — list all users ---

    private static async Task<Ok<PagedResult<AdminUserResponse>>> ListUsers(
        ApplicationDbContext db,
        HttpContext ctx,
        [AsParameters] AdminListRequest request)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        var query = db.Users.OrderByDescending(u => u.CreatedAt);

        // Фильтр по поиску (email или display name)
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.ToLower();
            query = (IOrderedQueryable<AppUser>)query.Where(u =>
                u.Email!.ToLower().Contains(search) ||
                u.DisplayName.ToLower().Contains(search));
        }

        var total = await query.CountAsync();
        var totalPages = (int)Math.Ceiling(total / (double)pageSize);

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new AdminUserResponse(
                u.Id,
                u.Email ?? "",
                u.DisplayName,
                u.IsAdmin,
                u.EmailConfirmed,
                u.NotifyByEmailOnCompletion,
                u.CreatedAt))
            .ToListAsync();

        var result = new PagedResult<AdminUserResponse>(items, page, pageSize, total, totalPages);
        return TypedResults.Ok(result);
    }

    // --- DELETE /api/admin/users/{userId} — delete a user (and their data) ---
    //
    // Каскадное удаление (cascade delete), реализованное вручную.
    //
    // Зачем вручную:
    // - EF Core каскад настроен на EventParticipant (FK), но не на TrackerEvent.OwnerUserId.
    // - При удалении пользователя Identity удаляет строку из AspNetUsers.
    // - Но TrackerEvent.OwnerUserId — это string FK без каскада в БД.
    //   Если не удалить события вручную, будет FK constraint violation.
    //
    // Порядок:
    // 1. Находим все события, где пользователь — владелец, удаляем.
    // 2. Находим все refresh-токены пользователя, удаляем (FK на AppUser).
    // 3. EventParticipant удалится каскадно через EF Core (FK с OnDelete.Cascade).
    // 4. Сохраняем всё ДО DeleteAsync(user), чтобы не получить FK-ошибку.
    // 5. Удаляем пользователя через UserManager.DeleteAsync.

    private static async Task<Results<NoContent, NotFound, BadRequest<ErrorResponse>>> DeleteUser(
        string userId,
        ApplicationDbContext db,
        UserManager<AppUser> userManager,
        HttpContext ctx)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
            return TypedResults.NotFound();

        // Cascade: удаляем события, где пользователь владелец
        var ownedEvents = await db.Events
            .Where(e => e.OwnerUserId == userId)
            .ToListAsync();
        db.Events.RemoveRange(ownedEvents);

        // Удаляем refresh-токены (FK-каскад на EventParticipant не затрагивает)
        var tokens = await db.RefreshTokens
            .Where(rt => rt.UserId == userId)
            .ToListAsync();
        db.RefreshTokens.RemoveRange(tokens);

        // EventParticipant удалится каскадом при DeleteAsync(user) через Identity
        // Сохраняем всё до удаления пользователя
        await db.SaveChangesAsync();

        var result = await userManager.DeleteAsync(user);
        if (!result.Succeeded)
            return TypedResults.BadRequest(new ErrorResponse(result.Errors.Select(e => e.Description).ToArray()));

        return TypedResults.NoContent();
    }

    // --- GET /api/admin/events — list all events ---

    private static async Task<Ok<PagedResult<AdminEventResponse>>> ListAllEvents(
        ApplicationDbContext db,
        [AsParameters] AdminListRequest request)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        var query = db.Events
            .Include(e => e.Owner)
            .Include(e => e.Category)
            .OrderByDescending(e => e.CreatedAt);

        var total = await query.CountAsync();
        var totalPages = (int)Math.Ceiling(total / (double)pageSize);

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new AdminEventResponse(
                e.Id,
                e.Title,
                e.Description,
                e.OwnerUserId,
                e.Owner!.DisplayName,
                e.Owner.Email!,
                e.Category != null ? e.Category.Name : null,
                e.CreatedAt))
            .ToListAsync();

        var result = new PagedResult<AdminEventResponse>(items, page, pageSize, total, totalPages);
        return TypedResults.Ok(result);
    }

    // --- DELETE /api/admin/events/{eventId} — delete any event ---

    private static async Task<Results<NoContent, NotFound>> DeleteAnyEvent(
        Guid eventId,
        ApplicationDbContext db)
    {
        var evt = await db.Events.FindAsync(eventId);
        if (evt is null)
            return TypedResults.NotFound();

        db.Events.Remove(evt);
        await db.SaveChangesAsync();

        return TypedResults.NoContent();
    }

    private static string GetUserId(HttpContext ctx)
    {
        return ctx.User.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)
            ?? ctx.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedAccessException("User ID not found in token.");
    }
}

// --- DTOs ---

/// <summary>Query params for admin list endpoints.</summary>
public record AdminListRequest(int Page = 1, int PageSize = 50, string? Search = null);

/// <summary>User info for admin dashboard.</summary>
public record AdminUserResponse(
    string Id,
    string Email,
    string DisplayName,
    bool IsAdmin,
    bool EmailConfirmed,
    bool NotifyByEmailOnCompletion,
    DateTime CreatedAt);

/// <summary>Event info for admin dashboard.</summary>
public record AdminEventResponse(
    Guid Id,
    string Title,
    string? Description,
    string OwnerUserId,
    string OwnerDisplayName,
    string OwnerEmail,
    string? CategoryName,
    DateTime CreatedAt);