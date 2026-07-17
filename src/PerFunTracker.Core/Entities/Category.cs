namespace PerFunTracker.Core.Entities;

/// <summary>
/// A user-created category for events (e.g. "Miss you", "Poop", "Workout").
/// Categories can be added over time — stored in DB, not a fixed enum.
/// </summary>
public class Category
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Display name of the category.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional emoji/icon for the category.</summary>
    public string? Icon { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
