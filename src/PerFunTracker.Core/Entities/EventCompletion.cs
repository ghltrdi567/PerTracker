namespace PerFunTracker.Core.Entities;

/// <summary>
/// A single completion log entry for an event — recorded whenever the event "occurs".
/// </summary>
public class EventCompletion
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid EventId { get; set; }
    public TrackerEvent? Event { get; set; }

    /// <summary>User who marked the event complete.</summary>
    public string CompletedByUserId { get; set; } = string.Empty;
    public AppUser? CompletedBy { get; set; }

    public DateTime CompletedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Optional note attached to this completion.</summary>
    public string? Note { get; set; }
}
