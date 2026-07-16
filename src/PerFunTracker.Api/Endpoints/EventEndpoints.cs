using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using PerFunTracker.Api.Models;
using PerFunTracker.Core.Entities;
using PerFunTracker.Core.Services;
using PerFunTracker.Infrastructure.Data;

namespace PerFunTracker.Api.Endpoints;

/// <summary>
/// Event and participant management endpoints.
/// Only event participants can access events; only owners can modify/delete.
/// </summary>
public static class EventEndpoints
{
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

    // --- GET /api/events — list events where current user is a participant ---

    private static async Task<Ok<List<EventResponse>>> ListEvents(
        ApplicationDbContext db,
        HttpContext ctx)
    {
        var userId = GetUserId(ctx);

        var events = await db.Events
            .Where(e => e.Participants.Any(p => p.UserId == userId))
            .Select(e => new EventResponse(
                e.Id,
                e.Title,
                e.Description,
                e.OwnerUserId,
                e.CreatedAt,
                e.Participants.Select(p => new ParticipantResponse(
                    p.UserId,
                    p.User!.DisplayName,
                    p.User!.Email!,
                    p.Role,
                    p.JoinedAt)).ToList()))
            .ToListAsync();

        return TypedResults.Ok(events);
    }

    // --- POST /api/events — create event, creator becomes Owner participant ---

    private static async Task<Results<Created<EventResponse>, BadRequest<ErrorResponse>>> CreateEvent(
        CreateEventRequest request,
        ApplicationDbContext db,
        HttpContext ctx)
    {
        var userId = GetUserId(ctx);

        var evt = new TrackerEvent
        {
            Title = request.Title,
            Description = request.Description,
            OwnerUserId = userId,
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
            [new ParticipantResponse(userId, user!.DisplayName, user!.Email!, ParticipantRole.Owner, evt.CreatedAt)]);

        return TypedResults.Created($"/api/events/{evt.Id}", response);
    }

    // --- GET /api/events/{id} — event details (participants only) ---

    private static async Task<Results<Ok<EventResponse>, NotFound>> GetEvent(
        Guid id,
        ApplicationDbContext db,
        HttpContext ctx)
    {
        var userId = GetUserId(ctx);

        var evt = await db.Events
            .Include(e => e.Participants)
                .ThenInclude(p => p.User)
            .FirstOrDefaultAsync(e => e.Id == id);

        if (evt is null)
            return TypedResults.NotFound();

        if (!evt.Participants.Any(p => p.UserId == userId))
            return TypedResults.NotFound(); // hide existence from non-participants

        var response = new EventResponse(
            evt.Id,
            evt.Title,
            evt.Description,
            evt.OwnerUserId,
            evt.CreatedAt,
            evt.Participants.Select(p => new ParticipantResponse(
                p.UserId,
                p.User!.DisplayName,
                p.User!.Email!,
                p.Role,
                p.JoinedAt)).ToList());

        return TypedResults.Ok(response);
    }

    // --- PUT /api/events/{id} — update event (owner only) ---

    private static async Task<Results<Ok<EventResponse>, NotFound, ForbidHttpResult>> UpdateEvent(
        Guid id,
        UpdateEventRequest request,
        ApplicationDbContext db,
        HttpContext ctx)
    {
        var userId = GetUserId(ctx);

        var evt = await db.Events
            .Include(e => e.Participants)
                .ThenInclude(p => p.User)
            .FirstOrDefaultAsync(e => e.Id == id);

        if (evt is null)
            return TypedResults.NotFound();

        if (evt.OwnerUserId != userId)
            return TypedResults.Forbid();

        evt.Title = request.Title;
        evt.Description = request.Description;

        await db.SaveChangesAsync();

        var response = new EventResponse(
            evt.Id,
            evt.Title,
            evt.Description,
            evt.OwnerUserId,
            evt.CreatedAt,
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
            .Include(e => e.Participants)
                .ThenInclude(p => p.User)
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
        await db.SaveChangesAsync();

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
        string userIdToRemove,
        ApplicationDbContext db,
        HttpContext ctx)
    {
        var currentUserId = GetUserId(ctx);

        var evt = await db.Events
            .Include(e => e.Participants)
            .FirstOrDefaultAsync(e => e.Id == id);

        if (evt is null)
            return TypedResults.NotFound();

        var participant = evt.Participants.FirstOrDefault(p => p.UserId == userIdToRemove);
        if (participant is null)
            return TypedResults.NotFound();

        // Owner can remove anyone; Members can only remove themselves
        var isOwner = evt.OwnerUserId == currentUserId;
        var isSelfRemoval = userIdToRemove == currentUserId;

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

    private static async Task<Results<Created<CompletionResponse>, NotFound>> MarkComplete(
        Guid id,
        MarkCompleteRequest request,
        ApplicationDbContext db,
        IEmailNotificationQueue notificationQueue,
        HttpContext ctx)
    {
        var userId = GetUserId(ctx);

        var evt = await db.Events
            .Include(e => e.Participants)
                .ThenInclude(p => p.User)
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

        // Enqueue email notifications for OTHER participants with the flag on
        foreach (var p in evt.Participants)
        {
            if (p.UserId == userId)
                continue; // skip the completer themselves

            if (!p.User!.NotifyByEmailOnCompletion)
                continue;

            var message = new EmailNotificationMessage(
                p.User.Email!,
                p.User.DisplayName,
                evt.Title,
                completer.DisplayName,
                completion.CompletedAt,
                request.Note);

            await notificationQueue.EnqueueAsync(message);
        }

        var response = new CompletionResponse(
            completion.Id,
            userId,
            completer.DisplayName,
            completion.CompletedAt,
            completion.Note);

        return TypedResults.Created($"/api/events/{evt.Id}/completions", response);
    }

    // --- GET /api/events/{id}/completions — completion history (participants only) ---

    private static async Task<Results<Ok<List<CompletionResponse>>, NotFound>> ListCompletions(
        Guid id,
        ApplicationDbContext db,
        HttpContext ctx)
    {
        var userId = GetUserId(ctx);

        // Verify the user is a participant
        var isParticipant = await db.EventParticipants.AnyAsync(p => p.EventId == id && p.UserId == userId);
        if (!isParticipant)
            return TypedResults.NotFound();

        var completions = await db.EventCompletions
            .Where(c => c.EventId == id)
            .OrderByDescending(c => c.CompletedAt)
            .Select(c => new CompletionResponse(
                c.Id,
                c.CompletedByUserId,
                c.CompletedBy!.DisplayName,
                c.CompletedAt,
                c.Note))
            .ToListAsync();

        return TypedResults.Ok(completions);
    }

    // --- Helper: extract user ID from JWT claims ---

    private static string GetUserId(HttpContext ctx)
    {
        return ctx.User.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)
            ?? ctx.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedAccessException("User ID not found in token.");
    }
}
