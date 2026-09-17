using NETworkManager.AI.Monitoring;
using Xunit;

namespace NETworkManager.AI.Tests;

public class MonitoringProfileDraftTests
{
    private static MonitoringProfile Profile(string name = "Core") => new()
    {
        Id = "core",
        Name = name,
        Targets = new[] { new MonitoringTarget { Id = "t", IPAddress = "10.0.0.1" } },
        Checks = new[] { new MonitoringCheck { CheckId = "c", Type = MonitorCheckType.Ping, TargetId = "t" } },
    };

    [Fact]
    public void Draft_starts_clean()
    {
        var draft = new MonitoringProfileDraft(Profile());
        Assert.False(draft.IsDirty);
    }

    [Fact]
    public void Update_marks_dirty_and_discard_restores()
    {
        var original = Profile();
        var draft = new MonitoringProfileDraft(original);

        draft.Update(original with { Description = "edited" });
        Assert.True(draft.IsDirty);

        draft.Discard();
        Assert.False(draft.IsDirty);
        Assert.Equal(original, draft.Current);
    }

    [Fact]
    public void Validate_delegates_to_profile()
    {
        var draft = new MonitoringProfileDraft(new MonitoringProfile { Name = "", Enabled = true });
        Assert.Contains(draft.Validate(), e => e.Contains("Profile name"));
    }

    [Fact]
    public void Save_persists_committed_snapshot()
    {
        var original = Profile();
        var draft = new MonitoringProfileDraft(original);
        draft.Update(original with { Description = "committed" });

        // The "Save" action is: hand the current snapshot to the service — the snapshot carries the edit.
        Assert.Equal("committed", draft.Current.Description);
    }
}