namespace PerFunTracker.Core.Entities;

/// <summary>
/// Junction entity linking a user to an event they participate in.
/// </summary>
public class EventParticipant
{
    public Guid EventId { get; set; }
    public TrackerEvent? Event { get; set; }

    public string UserId { get; set; } = string.Empty;
    public AppUser? User { get; set; }

    /// <summary>Role within the event (Owner or Member).</summary>
    public ParticipantRole Role { get; set; } = ParticipantRole.Member;

    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
}
