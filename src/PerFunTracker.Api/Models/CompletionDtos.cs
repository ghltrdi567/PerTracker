namespace PerFunTracker.Api.Models;

/// <summary>Request body for POST /api/events/{id}/complete</summary>
public record MarkCompleteRequest(string? Note);

/// <summary>Response for a completion log entry.</summary>
public record CompletionResponse(
    Guid Id,
    string CompletedByUserId,
    string CompletedByName,
    DateTime CompletedAt,
    string? Note);

/// <summary>Request/response for GET/PUT /api/users/me/settings</summary>
public record UserSettingsResponse(
    bool NotifyByEmailOnCompletion);
