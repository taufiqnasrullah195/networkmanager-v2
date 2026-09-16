using NETworkManager.AI.Monitoring;
using NETworkManager.AI.Tests.Fakes;
using Xunit;

namespace NETworkManager.AI.Tests;

public class MonitoringEngineTests
{
    private static MonitoringTarget Target(string id = "t1") => new() { Id = id, Name = "Gateway", IPAddress = "192.168.1.1" };

    private static MonitoringCheck Check(string id = "c1", string targetId = "t1", MonitorCheckType type = MonitorCheckType.Ping) => new()
    {
        CheckId = id,
        Type = type,
        TargetId = targetId,
        Timeout = TimeSpan.FromSeconds(5),
    };

    private static MonitoringResult Healthy(string checkId, string targetId) => new()
    {
        CheckId = checkId,
        TargetId = targetId,
        CheckType = MonitorCheckType.Ping,
        Status = MonitoringResultStatus.Healthy,
        Timestamp = DateTimeOffset.UtcNow,
        Duration = TimeSpan.Zero,
        SafeMessage = "healthy",
    };

    private static MonitoringResult Unhealthy(string checkId, string targetId) => new()
    {
        CheckId = checkId,
        TargetId = targetId,
        CheckType = MonitorCheckType.Ping,
        Status = MonitoringResultStatus.Unhealthy,
        ErrorClassification = MonitorErrorClass.Unreachable,
        Timestamp = DateTimeOffset.UtcNow,
        Duration = TimeSpan.Zero,
        SafeMessage = "unreachable",
    };

    private static (MonitoringEngine Engine, ScriptedMonitoringExecutor Executor) Build(MonitoringOptions? options = null)
    {
        var executor = new ScriptedMonitoringExecutor();
        var engine = new MonitoringEngine(options ?? new MonitoringOptions { MaxConcurrency = 4 }, executor);
        return (engine, executor);
    }

    [Fact]
    public async Task Add_and_remove_target()
    {
        var (engine, _) = Build();
        engine.AddTarget(Target("t1"));

        Assert.True(engine.RemoveTarget("t1"));
        Assert.False(engine.RemoveTarget("t1"));
        Assert.Null(engine.GetHealth("t1"));
    }

    [Fact]
    public void AddCheck_references_unknown_target_throws()
    {
        var (engine, _) = Build();
        Assert.Throws<ArgumentException>(() => engine.AddCheck(Check(targetId: "missing")));
    }

    [Fact]
    public void SetTargetEnabled_unknown_target_throws()
    {
        var (engine, _) = Build();
        Assert.Throws<ArgumentException>(() => engine.SetTargetEnabled("missing", false));
    }

    [Fact]
    public async Task RunCheck_records_and_emits_transition_then_no_duplicate()
    {
        var (engine, executor) = Build();
        var observer = new CapturingMonitoringObserver();
        engine.Subscribe(observer);

        engine.AddTarget(Target("t1"));
        engine.AddCheck(Check());

        executor.ResultFactory = (c, t) => Unhealthy(c.CheckId, t.Id);
        await engine.RunCheckAsync(Check());

        executor.ResultFactory = (c, t) => Healthy(c.CheckId, t.Id);
        await engine.RunCheckAsync(Check());
        await engine.RunCheckAsync(Check()); // unchanged → no new transition

        var changes = observer.StateChanges;
        Assert.Equal(2, changes.Count);
        Assert.Equal(NetworkHealthStatus.Unknown, changes[0].Previous);
        Assert.Equal(NetworkHealthStatus.Unhealthy, changes[0].New);
        Assert.Equal(NetworkHealthStatus.Unhealthy, changes[1].Previous);
        Assert.Equal(NetworkHealthStatus.Healthy, changes[1].New);

        Assert.Equal(NetworkHealthStatus.Healthy, engine.GetHealth("t1"));
    }

    [Fact]
    public async Task GetCurrentStatus_projects_snapshot()
    {
        var (engine, executor) = Build();
        executor.ResultFactory = (c, t) => Healthy(c.CheckId, t.Id);

        engine.AddTarget(Target("t1"));
        engine.AddCheck(Check("c1", "t1"));
        await engine.RunCheckAsync(Check("c1", "t1"));

        var status = engine.GetCurrentStatus();
        var snapshot = Assert.Single(status);

        Assert.Equal("Gateway", snapshot.DisplayName);
        Assert.Equal(NetworkHealthStatus.Healthy, snapshot.Health);
        Assert.Single(snapshot.Results);
    }

    [Fact]
    public async Task Recent_failures_exclude_healthy()
    {
        var (engine, executor) = Build();
        // unhealthy then healthy for the same single check type
        var call = 0;
        executor.ResultFactory = (c, t) => ++call == 1 ? Unhealthy(c.CheckId, t.Id) : Healthy(c.CheckId, t.Id);

        engine.AddTarget(Target());
        engine.AddCheck(Check());
        await engine.RunCheckAsync(Check());
        await engine.RunCheckAsync(Check());

        var failures = engine.GetRecentFailures(10);
        Assert.Single(failures);
        Assert.Equal(MonitoringResultStatus.Unhealthy, failures[0].Status);
    }

    [Fact]
    public async Task Periodic_scheduling_runs_then_stops_gracefully()
    {
        var (engine, executor) = Build(new MonitoringOptions { DefaultInterval = TimeSpan.FromMilliseconds(100), MaxConcurrency = 2 });

        engine.AddTarget(Target());
        engine.AddCheck(Check());

        await engine.StartAsync();
        await Task.Delay(400);
        await engine.StopAsync();

        Assert.True(executor.Executions >= 2, $"expected >= 2 executions, got {executor.Executions}");

        var after = executor.Executions;
        await Task.Delay(250);
        Assert.Equal(after, executor.Executions); // nothing ran after stop
    }

    [Fact]
    public async Task Concurrency_limit_is_respected()
    {
        var (engine, executor) = Build(new MonitoringOptions { DefaultInterval = TimeSpan.FromMilliseconds(50), MaxConcurrency = 2 });
        executor.Delay = TimeSpan.FromMilliseconds(80);

        for (var i = 0; i < 6; i++)
        {
            engine.AddTarget(Target($"t{i}"));
            engine.AddCheck(Check($"c{i}", $"t{i}"));
        }

        await engine.StartAsync();
        await Task.Delay(500);
        await engine.StopAsync();

        Assert.True(executor.MaxRunning <= 2, $"max concurrent was {executor.MaxRunning}, expected <= 2");
        Assert.True(executor.Executions >= 6, $"expected >= 6 executions, got {executor.Executions}");
    }

    [Fact]
    public async Task Disabled_target_is_not_scheduled()
    {
        var (engine, executor) = Build(new MonitoringOptions { DefaultInterval = TimeSpan.FromMilliseconds(50) });

        engine.AddTarget(Target());
        engine.AddCheck(Check());
        engine.SetTargetEnabled("t1", false);

        await engine.StartAsync();
        await Task.Delay(200);
        await engine.StopAsync();

        Assert.Equal(0, executor.Executions);
    }

    [Fact]
    public async Task StopAsync_completes_promptly_while_a_check_is_in_flight()
    {
        var (engine, executor) = Build(new MonitoringOptions { DefaultInterval = TimeSpan.FromMilliseconds(50) });
        executor.Delay = TimeSpan.FromSeconds(5);

        engine.AddTarget(Target());
        engine.AddCheck(Check());

        await engine.StartAsync();
        await Task.Delay(100); // let one check start and block on its 5s delay

        var stopTask = engine.StopAsync();
        var completed = await Task.WhenAny(stopTask, Task.Delay(TimeSpan.FromSeconds(2)));

        // StopAsync must return before the 2s watchdog (in-flight check is cancelled, not awaited to completion).
        Assert.Same(stopTask, completed);
    }

    [Fact]
    public async Task No_secrets_in_logger_output()
    {
        var (engine, executor) = Build();
        var logger = new CapturingMonitoringLogger();
        var observer = new CapturingMonitoringObserver();

        // Rebuild with the capturing logger.
        var captureEngine = new MonitoringEngine(new MonitoringOptions(), executor, logger: logger);
        captureEngine.Subscribe(observer);
        executor.ResultFactory = (c, t) => Unhealthy(c.CheckId, t.Id);

        captureEngine.AddTarget(new MonitoringTarget { Id = "t1", IPAddress = "10.0.0.1" });
        captureEngine.AddCheck(Check());
        await captureEngine.StartAsync();
        await captureEngine.StopAsync();
        await captureEngine.RunCheckAsync(Check());

        Assert.NotEmpty(logger.Lines);
        Assert.DoesNotContain(logger.Lines, l => l.Contains("password", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(logger.Lines, l => l.Contains("token", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(logger.Lines, l => l.Contains("secret", StringComparison.OrdinalIgnoreCase));
    }
}