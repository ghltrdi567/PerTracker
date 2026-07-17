namespace PerFunTracker.Core.Entities;

/// <summary>
/// A recurring or ongoing event (e.g. "Take out trash", "Call Mom").
/// Has no scheduled occurrence — participants log completions as they happen.
/// </summary>
public class TrackerEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Title of the event.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Optional description.</summary>
    public string? Description { get; set; }

    /// <summary>Optional category ID (null = uncategorized).</summary>
    public Guid? CategoryId { get; set; }
    public Category? Category { get; set; }

    /// <summary>User who created the event.</summary>
    public string OwnerUserId { get; set; } = string.Empty;

    public AppUser? Owner { get; set; }

    /// <summary>When the event was created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Participants of this event.</summary>
    public ICollection<EventParticipant> Participants { get; set; } = [];

    /// <summary>Completion log entries.</summary>
    public ICollection<EventCompletion> Completions { get; set; } = [];
}
