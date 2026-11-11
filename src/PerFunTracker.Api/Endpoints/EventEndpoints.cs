using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PerFunTracker.Api.Models;
using PerFunTracker.Core.Entities;
using PerFunTracker.Core.Options;
using PerFunTracker.Core.Services;
using PerFunTracker.Infrastructure.Data;

namespace PerFunTracker.Api.Endpoints;

/// <summary>
/// Event and participant management endpoints.
/// Only event participants can access events; only owners can modify/delete.
/// </summary>
public static class EventEndpoints
{
    private const int MaxPageSize = 100;

    public static IEndpointRouteBuilder MapEventEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/events").RequireAuthorization();

        group.MapGet("/", ListEvents);
        group.MapPost("/", CreateEvent);
        group.MapGet("/{id:guid}", GetEvent);
        group.MapPut("/{id:guid}", UpdateEvent);
        group.MapDelete("/{id:guid}", DeleteEvent);
        group.MapPost("/{id:guid}/participants", AddParticipant);
        group.MapDelete("/{id:guid}/participants/{userId}", RemoveParticipant);
        group.MapPost("/{id:guid}/complete", MarkComplete);
        group.MapGet("/{id:guid}/completions", ListCompletions);

        return app;
    }

    // --- GET /api/events — paginated list where current user is a participant ---
    // Дебаг-режим (Debug__SeeAllEvents): админ получает ВСЕ события, не только свои.

    private static async Task<Ok<PagedResult<EventResponse>>> ListEvents(
        ApplicationDbContext db,
        DebugOptions debug,
        HttpContext ctx,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var userId = GetUserId(ctx);
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        IQueryable<TrackerEvent> baseQuery = db.Events;
        if (!AdminSeesAll(debug, ctx))
            baseQuery = baseQuery.Where(e => e.Participants.Any(p => p.UserId == userId));

        var total = await baseQuery.CountAsync();
        var totalPages = (int)Math.Ceiling(total / (double)pageSize);

        var events = await baseQuery
            .Include(e => e.Participants).ThenInclude(p => p.User)
            .Include(e => e.Category)
            .OrderByDescending(e => e.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var items = events.Select(e => new EventResponse(
            e.Id,
            e.Title,
            e.Description,
            e.OwnerUserId,
            e.CreatedAt,
            e.Category != null ? new CategoryResponse(e.Category.Id, e.Category.Name, e.Category.Icon) : null,
            e.Participants.Select(p => new ParticipantResponse(
                p.UserId,
                p.User!.DisplayName,
                p.User!.Email!,
                p.Role,
                p.JoinedAt)).ToList())).ToList();

        var result = new PagedResult<EventResponse>(items, page, pageSize, total, totalPages);
        return TypedResults.Ok(result);
    }

    // --- POST /api/events — create event, creator becomes Owner participant ---

    private static async Task<Results<Created<EventResponse>, BadRequest<ErrorResponse>>> CreateEvent(
        CreateEventRequest request,
        ApplicationDbContext db,
        HttpContext ctx)
    {
        var userId = GetUserId(ctx);

        // Валидация categoryId: если категория не существует, SaveChangesAsync
        // упал бы с FK-нарушением → 500. Возвращаем 400 до записи в БД.
        if (request.CategoryId is { } categoryId &&
            !await db.Categories.AnyAsync(c => c.Id == categoryId))
        {
            return TypedResults.BadRequest(new ErrorResponse(["Category not found."]));
        }

        // Валидация длины: переполнение varchar в Postgres (22001) даёт 500.
        if (request.Title?.Length > TrackerEvent.TitleMaxLength)
            return TypedResults.BadRequest(new ErrorResponse([$"Title must be at most {TrackerEvent.TitleMaxLength} characters."]));
        if (request.Description?.Length > TrackerEvent.DescriptionMaxLength)
            return TypedResults.BadRequest(new ErrorResponse([$"Description must be at most {TrackerEvent.DescriptionMaxLength} characters."]));

        var evt = new TrackerEvent
        {
            Title = request.Title,
            Description = request.Description,
            OwnerUserId = userId,
            CategoryId = request.CategoryId,
            CreatedAt = DateTime.UtcNow,
        };

        // Creator is automatically an Owner-level participant
        evt.Participants.Add(new EventParticipant
        {
            UserId = userId,
            Role = ParticipantRole.Owner,
            JoinedAt = DateTime.UtcNow,
        });

        db.Events.Add(evt);
        await db.SaveChangesAsync();

        // Load the participant's user info for the response
        var user = await db.Users.FindAsync(userId);

        var response = new EventResponse(
            evt.Id,
            evt.Title,
            evt.Description,
            evt.OwnerUserId,
            evt.CreatedAt,
            evt.Category != null ? new CategoryResponse(evt.Category.Id, evt.Category.Name, evt.Category.Icon) : null,
            [new ParticipantResponse(userId, user!.DisplayName, user!.Email!, ParticipantRole.Owner, evt.CreatedAt)]);

        return TypedResults.Created($"/api/events/{evt.Id}", response);
    }

    // --- GET /api/events/{id} — event details (participants only) ---
    //
    // Модель авторизации: 404 вместо 403 для не-участников.
    //
    // Почему 404, а не 403:
    // - 403 (Forbidden) говорит: "я знаю, что такое событие существует, но ты не можешь".
    // - 404 (Not Found) говорит: "я не знаю, о чём ты".
    // - Разница в утечке информации: 403 раскрывает существование приватного события.
    // - Злоумышленник может перебирать ID событий и узнавать, какие существуют.
    // - Поэтому все ошибки доступа к событиям — 404 (существование скрыто).
    //
    // Исключений нет — даже если ссылка на событие пришла в уведомлении,
    // не-участник должен получить 404, чтобы не раскрывать факт существования события.

    private static async Task<Results<Ok<EventResponse>, NotFound>> GetEvent(
        Guid id,
        ApplicationDbContext db,
        DebugOptions debug,
        HttpContext ctx)
    {
        var userId = GetUserId(ctx);

        var evt = await db.Events
            .Include(e => e.Participants).ThenInclude(p => p.User)
            .Include(e => e.Category)
            .FirstOrDefaultAsync(e => e.Id == id);

        if (evt is null)
            return TypedResults.NotFound();

        // Дебаг-режим: админ читает чужие события; иначе существование скрыто (404)
        if (!AdminSeesAll(debug, ctx) && !evt.Participants.Any(p => p.UserId == userId))
            return TypedResults.NotFound(); // hide existence from non-participants

        var response = new EventResponse(
            evt.Id,
            evt.Title,
            evt.Description,
            evt.OwnerUserId,
            evt.CreatedAt,
            evt.Category != null ? new CategoryResponse(evt.Category.Id, evt.Category.Name, evt.Category.Icon) : null,
            evt.Participants.Select(p => new ParticipantResponse(
                p.UserId,
                p.User!.DisplayName,
                p.User!.Email!,
                p.Role,
                p.JoinedAt)).ToList());

        return TypedResults.Ok(response);
    }

    // --- PUT /api/events/{id} — update event (owner only) ---

    private static async Task<Results<Ok<EventResponse>, NotFound, ForbidHttpResult, BadRequest<ErrorResponse>>> UpdateEvent(
        Guid id,
        UpdateEventRequest request,
        ApplicationDbContext db,
        HttpContext ctx)
    {
        var userId = GetUserId(ctx);

        var evt = await db.Events
            .Include(e => e.Participants).ThenInclude(p => p.User)
            .Include(e => e.Category)
            .FirstOrDefaultAsync(e => e.Id == id);

        if (evt is null)
            return TypedResults.NotFound();

        if (evt.OwnerUserId != userId)
            return TypedResults.Forbid();

        // Тот же фикс, что и в CreateEvent: несуществующий categoryId → FK-нарушение → 500
        if (request.CategoryId is { } categoryId &&
            !await db.Categories.AnyAsync(c => c.Id == categoryId))
        {
            return TypedResults.BadRequest(new ErrorResponse(["Category not found."]));
        }

        if (request.Title?.Length > TrackerEvent.TitleMaxLength)
            return TypedResults.BadRequest(new ErrorResponse([$"Title must be at most {TrackerEvent.TitleMaxLength} characters."]));
        if (request.Description?.Length > TrackerEvent.DescriptionMaxLength)
            return TypedResults.BadRequest(new ErrorResponse([$"Description must be at most {TrackerEvent.DescriptionMaxLength} characters."]));

        evt.Title = request.Title;
        evt.Description = request.Description;
        evt.CategoryId = request.CategoryId;

        await db.SaveChangesAsync();

        // После смены FK навигация Category не перезагрузится сама — без этого
        // ответ нёс бы старую (или null) категорию при актуальной новой в БД.
        await db.Entry(evt).Reference(e => e.Category).LoadAsync();

        var response = new EventResponse(
            evt.Id,
            evt.Title,
            evt.Description,
            evt.OwnerUserId,
            evt.CreatedAt,
            evt.Category != null ? new CategoryResponse(evt.Category.Id, evt.Category.Name, evt.Category.Icon) : null,
            evt.Participants.Select(p => new ParticipantResponse(
                p.UserId,
                p.User!.DisplayName,
                p.User!.Email!,
                p.Role,
                p.JoinedAt)).ToList());

        return TypedResults.Ok(response);
    }

    // --- DELETE /api/events/{id} — delete event (owner only) ---

    private static async Task<Results<NoContent, NotFound, ForbidHttpResult>> DeleteEvent(
        Guid id,
        ApplicationDbContext db,
        HttpContext ctx)
    {
        var userId = GetUserId(ctx);

        var evt = await db.Events.FirstOrDefaultAsync(e => e.Id == id);
        if (evt is null)
            return TypedResults.NotFound();

        if (evt.OwnerUserId != userId)
            return TypedResults.Forbid();

        db.Events.Remove(evt);
        await db.SaveChangesAsync();

        return TypedResults.NoContent();
    }

    // --- POST /api/events/{id}/participants — add participant by email (owner only) ---

    private static async Task<Results<Ok<ParticipantResponse>, NotFound, ForbidHttpResult, BadRequest<ErrorResponse>>> AddParticipant(
        Guid id,
        AddParticipantRequest request,
        ApplicationDbContext db,
        UserManager<AppUser> userManager,
        HttpContext ctx)
    {
        var userId = GetUserId(ctx);

        var evt = await db.Events
            .Include(e => e.Participants).ThenInclude(p => p.User)
            .Include(e => e.Category)
            .FirstOrDefaultAsync(e => e.Id == id);

        if (evt is null)
            return TypedResults.NotFound();

        if (evt.OwnerUserId != userId)
            return TypedResults.Forbid();

        var userToAdd = await userManager.FindByEmailAsync(request.Email);
        if (userToAdd is null)
            return TypedResults.BadRequest(new ErrorResponse(["User with this email not found. They must register first."]));

        if (evt.Participants.Any(p => p.UserId == userToAdd.Id))
            return TypedResults.BadRequest(new ErrorResponse(["User is already a participant."]));

        var participant = new EventParticipant
        {
            EventId = evt.Id,
            UserId = userToAdd.Id,
            Role = ParticipantRole.Member,
            JoinedAt = DateTime.UtcNow,
        };

        evt.Participants.Add(participant);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is NpgsqlException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
            })
        {
            // Два одновременных запроса прошли проверку Any() выше (оба увидели
            // «нет участника») и оба дошли до INSERT — UNIQUE-нарушение в БД
            // означает «участник уже добавлен». Возвращаем тот же 400, что и
            // при последовательном повторе, вместо необработанного 500.
            return TypedResults.BadRequest(new ErrorResponse(["User is already a participant."]));
        }

        var response = new ParticipantResponse(
            userToAdd.Id,
            userToAdd.DisplayName,
            userToAdd.Email!,
            ParticipantRole.Member,
            participant.JoinedAt);

        return TypedResults.Ok(response);
    }

    // --- DELETE /api/events/{id}/participants/{userId} — remove participant ---

    private static async Task<Results<NoContent, NotFound, ForbidHttpResult, BadRequest<ErrorResponse>>> RemoveParticipant(
        Guid id,
        string userId,
        ApplicationDbContext db,
        HttpContext ctx)
    {
        var currentUserId = GetUserId(ctx);

        var evt = await db.Events
            .Include(e => e.Participants)
            .FirstOrDefaultAsync(e => e.Id == id);

        if (evt is null)
            return TypedResults.NotFound();

        var participant = evt.Participants.FirstOrDefault(p => p.UserId == userId);
        if (participant is null)
            return TypedResults.NotFound();

        // Owner can remove anyone; Members can only remove themselves
        var isOwner = evt.OwnerUserId == currentUserId;
        var isSelfRemoval = userId == currentUserId;

        if (!isOwner && !isSelfRemoval)
            return TypedResults.Forbid();

        // Can't remove the owner — transfer ownership or delete the event instead
        if (participant.Role == ParticipantRole.Owner)
            return TypedResults.BadRequest(new ErrorResponse(["Cannot remove the event owner. Delete the event instead."]));

        evt.Participants.Remove(participant);
        await db.SaveChangesAsync();

        return TypedResults.NoContent();
    }

    // --- POST /api/events/{id}/complete — mark event complete (participants only) ---

    private static async Task<Results<Created<CompletionResponse>, NotFound, BadRequest<ErrorResponse>>> MarkComplete(
        Guid id,
        MarkCompleteRequest request,
        ApplicationDbContext db,
        IEmailNotificationQueue notificationQueue,
        HttpContext ctx)
    {
        var userId = GetUserId(ctx);

        // Переполнение varchar в Postgres (22001) даёт 500 — отсекаем до записи.
        if (request.Note?.Length > EventCompletion.NoteMaxLength)
            return TypedResults.BadRequest(new ErrorResponse([$"Note must be at most {EventCompletion.NoteMaxLength} characters."]));

        var evt = await db.Events
            .Include(e => e.Participants).ThenInclude(p => p.User)
            .Include(e => e.Category)
            .FirstOrDefaultAsync(e => e.Id == id);

        if (evt is null)
            return TypedResults.NotFound();

        // Only participants can mark complete
        var participant = evt.Participants.FirstOrDefault(p => p.UserId == userId);
        if (participant is null)
            return TypedResults.NotFound();

        var completer = participant.User!;

        var completion = new EventCompletion
        {
            EventId = evt.Id,
            CompletedByUserId = userId,
            CompletedAt = DateTime.UtcNow,
            Note = request.Note,
        };

        db.EventCompletions.Add(completion);
        await db.SaveChangesAsync();

        // HTML собирается здесь (у enqueue-сайта) — воркер только отправляет
        var html = $"""
            <h3>Event marked complete: {HtmlEncoder.Default.Encode(evt.Title)}</h3>
            <p><strong>{HtmlEncoder.Default.Encode(completer.DisplayName)}</strong> marked this event as complete on
               {completion.CompletedAt:yyyy-MM-dd HH:mm} UTC.</p>
            {(request.Note is not null ? $"<p><em>Note:</em> {HtmlEncoder.Default.Encode(request.Note)}</p>" : "")}
            <p style="color:#888;font-size:12px;">You received this email because you participate in this event
               and have email notifications enabled.</p>
            """;

        // Enqueue email notifications for OTHER participants with the flag on
        foreach (var p in evt.Participants)
        {
            if (p.UserId == userId)
                continue; // skip the completer themselves

            if (!p.User!.NotifyByEmailOnCompletion)
                continue;

            await notificationQueue.EnqueueAsync(new EmailNotificationMessage(
                p.User.Email!,
                $"Event complete: {evt.Title}",
                html));
        }

        var response = new CompletionResponse(
            completion.Id,
            userId,
            completer.DisplayName,
            completion.CompletedAt,
            completion.Note);

        return TypedResults.Created($"/api/events/{evt.Id}/completions", response);
    }

    // --- GET /api/events/{id}/completions — paginated completion history ---

    private static async Task<Results<Ok<PagedResult<CompletionResponse>>, NotFound>> ListCompletions(
        Guid id,
        ApplicationDbContext db,
        DebugOptions debug,
        HttpContext ctx,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var userId = GetUserId(ctx);
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        // Дебаг-режим: админ читает завершения любого события
        var canView = AdminSeesAll(debug, ctx)
            || await db.EventParticipants.AnyAsync(p => p.EventId == id && p.UserId == userId);
        if (!canView)
            return TypedResults.NotFound(); // hide existence from non-participants

        var baseQuery = db.EventCompletions
            .Where(c => c.EventId == id);

        var total = await baseQuery.CountAsync();
        var totalPages = (int)Math.Ceiling(total / (double)pageSize);

        var items = await baseQuery
            .OrderByDescending(c => c.CompletedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new CompletionResponse(
                c.Id,
                c.CompletedByUserId,
                c.CompletedBy!.DisplayName,
                c.CompletedAt,
                c.Note))
            .ToListAsync();

        var result = new PagedResult<CompletionResponse>(items, page, pageSize, total, totalPages);
        return TypedResults.Ok(result);
    }

    // --- Helpers ---

    // Дебаг-режим «админ видит все»: флаг включается при деплое (Debug__SeeAllEvents,
    // см. DebugOptions). Claim-проверка та же, что у политики "Admin" в Program.cs.
    private static bool AdminSeesAll(DebugOptions debug, HttpContext ctx)
        => debug.SeeAllEvents && ctx.User.HasClaim("is_admin", "true");

    // --- Helper: extract user ID from JWT claims ---

    private static string GetUserId(HttpContext ctx)
    {
        return ctx.User.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)
            ?? ctx.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedAccessException("User ID not found in token.");
    }
}
