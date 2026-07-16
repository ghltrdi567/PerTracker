using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using PerFunTracker.Core.Entities;

namespace PerFunTracker.Infrastructure.Data;

/// <summary>
/// Application DbContext, extends IdentityDbContext with custom entities.
/// </summary>
public class ApplicationDbContext : IdentityDbContext<AppUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options) { }

    public DbSet<TrackerEvent> Events => Set<TrackerEvent>();
    public DbSet<EventParticipant> EventParticipants => Set<EventParticipant>();
    public DbSet<EventCompletion> EventCompletions => Set<EventCompletion>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // TrackerEvent
        builder.Entity<TrackerEvent>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).IsRequired().HasMaxLength(200);
            e.Property(x => x.Description).HasMaxLength(2000);
            e.Property(x => x.OwnerUserId).IsRequired();
            e.Property(x => x.CreatedAt).IsRequired();

            e.HasOne(x => x.Owner)
                .WithMany()
                .HasForeignKey(x => x.OwnerUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // EventParticipant — composite key (EventId, UserId)
        builder.Entity<EventParticipant>(e =>
        {
            e.HasKey(x => new { x.EventId, x.UserId });

            e.HasOne(x => x.Event)
                .WithMany(ev => ev.Participants)
                .HasForeignKey(x => x.EventId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            e.Property(x => x.Role).HasConversion<int>();
            e.Property(x => x.JoinedAt).IsRequired();
        });

        // EventCompletion
        builder.Entity<EventCompletion>(e =>
        {
            e.HasKey(x => x.Id);

            e.HasOne(x => x.Event)
                .WithMany(ev => ev.Completions)
                .HasForeignKey(x => x.EventId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.CompletedBy)
                .WithMany()
                .HasForeignKey(x => x.CompletedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            e.Property(x => x.CompletedByUserId).IsRequired();
            e.Property(x => x.CompletedAt).IsRequired();
            e.Property(x => x.Note).HasMaxLength(1000);
        });

        // RefreshToken
        builder.Entity<RefreshToken>(e =>
        {
            e.HasKey(x => x.Id);

            e.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            e.Property(x => x.Token).IsRequired().HasMaxLength(256);
            e.Property(x => x.UserId).IsRequired();
            e.Property(x => x.ExpiresAt).IsRequired();
            e.Property(x => x.CreatedAt).IsRequired();

            e.HasIndex(x => x.Token);
        });
    }
}
