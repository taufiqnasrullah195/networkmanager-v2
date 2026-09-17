using NETworkManager.AI.Monitoring;
using NETworkManager.AI.Tests.Fakes;
using Xunit;

namespace NETworkManager.AI.Tests;

public class MonitoringConfigurationApplierTests
{
    private static MonitoringEngine Engine() =>
        new(new MonitoringOptions { MaxConcurrency = 2 }, new ScriptedMonitoringExecutor());

    private static MonitoringProfile Profile(string id, bool enabled = true, int targets = 1, int checks = 1) =>
        new()
        {
            Id = id,
            Name = id,
            Enabled = enabled,
            DefaultInterval = TimeSpan.FromSeconds(45),
            DefaultTimeout = TimeSpan.FromSeconds(7),
            Targets = Enumerable.Range(1, targets)
                .Select(i => new MonitoringTarget { Id = $"{id}-t{i}", IPAddress = $"10.0.0.{i}" })
                .ToList(),
            Checks = Enumerable.Range(1, checks)
                .Select(i => new MonitoringCheck { CheckId = $"{id}-c{i}", Type = MonitorCheckType.Ping, TargetId = $"{id}-t1" })
                .ToList(),
        };

    [Fact]
    public void Apply_clears_and_adds_enabled_profiles()
    {
        var engine = Engine();
        MonitoringConfigurationApplier.Apply(engine, new[] { Profile("core") });

        Assert.Equal(1, engine.GetCurrentStatus().Count);
    }

    [Fact]
    public void Disabled_profiles_are_skipped()
    {
        var engine = Engine();
        MonitoringConfigurationApplier.Apply(engine, new[] { Profile("core", enabled: false) });

        Assert.Empty(engine.GetCurrentStatus());
    }

    [Fact]
    public void Reapply_does_not_create_duplicate_targets()
    {
        var engine = Engine();
        MonitoringConfigurationApplier.Apply(engine, new[] { Profile("core", targets: 3) });
        var first = engine.GetCurrentStatus().Count;

        MonitoringConfigurationApplier.Apply(engine, new[] { Profile("core", targets: 3) });

        Assert.Equal(first, engine.GetCurrentStatus().Count);
    }

    [Fact]
    public void Profile_defaults_are_resolved_into_checks()
    {
        var profile = Profile("core"); // DefaultInterval=45s, DefaultTimeout=7s
        var check = new MonitoringCheck { CheckId = "c", Type = MonitorCheckType.Ping, TargetId = "core-t1" };

        var resolved = MonitoringConfigurationApplier.Resolve(profile, check);

        Assert.Equal(TimeSpan.FromSeconds(45), resolved.Interval);
        Assert.Equal(TimeSpan.FromSeconds(7), resolved.Timeout);
    }

    [Fact]
    public void Explicit_check_values_override_profile_defaults()
    {
        var profile = Profile("core");
        var check = new MonitoringCheck { CheckId = "c", Type = MonitorCheckType.Ping, TargetId = "core-t1", Timeout = TimeSpan.FromSeconds(2), Interval = TimeSpan.FromSeconds(9) };

        var resolved = MonitoringConfigurationApplier.Resolve(profile, check);

        Assert.Equal(TimeSpan.FromSeconds(2), resolved.Timeout);
        Assert.Equal(TimeSpan.FromSeconds(9), resolved.Interval);
    }

    [Fact]
    public void Clear_removes_all_targets_and_state()
    {
        var engine = Engine();
        MonitoringConfigurationApplier.Apply(engine, new[] { Profile("core", targets: 2) });
        Assert.Equal(2, engine.GetCurrentStatus().Count);

        engine.Clear();
        Assert.Empty(engine.GetCurrentStatus());
    }

    [Fact]
    public async Task Start_is_idempotent_and_stop_cleans_up()
    {
        var engine = Engine();
        MonitoringConfigurationApplier.Apply(engine, new[] { Profile("core") });

        await engine.StartAsync();
        await engine.StartAsync(); // second start is a no-op
        Assert.True(engine.IsRunning);

        await engine.StopAsync();
        Assert.False(engine.IsRunning);
    }
}