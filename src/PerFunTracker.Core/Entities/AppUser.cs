using Microsoft.AspNetCore.Identity;

namespace PerFunTracker.Core.Entities;

/// <summary>
/// Application user, extends ASP.NET Core Identity's <see cref="IdentityUser"/>.
/// </summary>
public class AppUser : IdentityUser
{
    /// <summary>Display name shown to other participants.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Whether the user wants email notifications when an event is marked complete.</summary>
    public bool NotifyByEmailOnCompletion { get; set; } = true;

    /// <summary>Whether the user has admin privileges (e.g. category management).</summary>
    public bool IsAdmin { get; set; } = false;

    /// <summary>When the user account was created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
