using PerFunTracker.Core.Entities;

namespace PerFunTracker.Api.Models;

// --- Event ---

/// <summary>Request body for POST /api/events</summary>
public record CreateEventRequest(string Title, string? Description);

/// <summary>Request body for PUT /api/events/{id}</summary>
public record UpdateEventRequest(string Title, string? Description);

/// <summary>Response for event list and detail.</summary>
public record EventResponse(
    Guid Id,
    string Title,
    string? Description,
    string OwnerUserId,
    DateTime CreatedAt,
    List<ParticipantResponse> Participants);

/// <summary>Response for participant info within an event.</summary>
public record ParticipantResponse(
    string UserId,
    string DisplayName,
    string Email,
    ParticipantRole Role,
    DateTime JoinedAt);

// --- Participants ---

/// <summary>Request body for POST /api/events/{id}/participants</summary>
public record AddParticipantRequest(string Email);
