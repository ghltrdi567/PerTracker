using PerFunTracker.Core.Entities;

namespace PerFunTracker.Tests;

public class TrackerEventTests
{
    [Fact]
    public void TrackerEvent_Defaults_AreSet()
    {
        var evt = new TrackerEvent();

        Assert.NotEqual(Guid.Empty, evt.Id);
        Assert.Equal(string.Empty, evt.Title);
        Assert.Null(evt.Description);
        Assert.Equal(string.Empty, evt.OwnerUserId);
        Assert.Empty(evt.Participants);
        Assert.Empty(evt.Completions);
    }

    [Fact]
    public void AppUser_Defaults_AreSet()
    {
        var user = new AppUser();

        Assert.Equal(string.Empty, user.DisplayName);
        Assert.True(user.NotifyByEmailOnCompletion);
    }

    [Fact]
    public void EventParticipant_DefaultRole_IsMember()
    {
        var participant = new EventParticipant();

        Assert.Equal(ParticipantRole.Member, participant.Role);
    }

    [Fact]
    public void EventCompletion_Defaults_AreSet()
    {
        var completion = new EventCompletion();

        Assert.NotEqual(Guid.Empty, completion.Id);
        Assert.Null(completion.Note);
    }
}
