using PerFunTracker.Core.Entities;

namespace PerFunTracker.Api.Models;

// --- Event ---

/// <summary>Request body for POST /api/events</summary>
public record CreateEventRequest(string Title, string? Description, Guid? CategoryId);

/// <summary>Request body for PUT /api/events/{id}</summary>
public record UpdateEventRequest(string Title, string? Description, Guid? CategoryId);

/// <summary>Response for event list and detail.</summary>
public record EventResponse(
    Guid Id,
    string Title,
    string? Description,
    string OwnerUserId,
    DateTime CreatedAt,
    CategoryResponse? Category,
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

// --- Category ---

/// <summary>Response for a category.</summary>
public record CategoryResponse(Guid Id, string Name, string? Icon);

/// <summary>Request body for POST /api/categories</summary>
public record CreateCategoryRequest(string Name, string? Icon);

// --- Pagination ---

/// <summary>Generic paged response wrapper.</summary>
public record PagedResult<T>(
    List<T> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

