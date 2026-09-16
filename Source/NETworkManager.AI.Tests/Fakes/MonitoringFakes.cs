using NETworkManager.AI.Abstractions;
using NETworkManager.AI.Execution;
using NETworkManager.AI.Models;
using NETworkManager.AI.Monitoring;
using NETworkManager.AI.Registry;

namespace NETworkManager.AI.Tests.Fakes;

/// <summary>Controllable <see cref="IMonitoringCheckExecutor"/> for scheduler/engine tests (counts, max-concurrency, delay).</summary>
public sealed class ScriptedMonitoringExecutor : IMonitoringCheckExecutor
{
    private int _running;
    private int _maxRunning;
    private int _executions;

    public TimeSpan Delay { get; set; }

    public Func<MonitoringCheck, MonitoringTarget, MonitoringResult>? ResultFactory { get; set; }

    public int Executions => Volatile.Read(ref _executions);

    public int MaxRunning => Volatile.Read(ref _maxRunning);

    public async Task<MonitoringResult> ExecuteAsync(MonitoringCheck check, MonitoringTarget target, CancellationToken cancellationToken)
    {
        var running = Interlocked.Increment(ref _running);
        UpdateMax(running);

        try
        {
            if (Delay > TimeSpan.Zero)
                await Task.Delay(Delay, cancellationToken).ConfigureAwait(false);

            return ResultFactory?.Invoke(check, target) ?? Healthy(check, target);
        }
        finally
        {
            Interlocked.Decrement(ref _running);
            Interlocked.Increment(ref _executions);
        }
    }

    private void UpdateMax(int value)
    {
        int current;

        do
        {
            current = Volatile.Read(ref _maxRunning);

            if (value <= current)
                return;
        }
        while (Interlocked.CompareExchange(ref _maxRunning, value, current) != current);
    }

    private static MonitoringResult Healthy(MonitoringCheck check, MonitoringTarget target) => new()
    {
        CheckId = check.CheckId,
        TargetId = target.Id,
        CheckType = check.Type,
        Status = MonitoringResultStatus.Healthy,
        ErrorClassification = MonitorErrorClass.None,
        Timestamp = DateTimeOffset.UtcNow,
        Duration = TimeSpan.Zero,
        SafeMessage = "healthy",
        CorrelationId = check.CheckId,
    };
}

/// <summary>Captures decoded monitoring events + state changes and logger calls for assertions.</summary>
public sealed class CapturingMonitoringObserver : IMonitoringObserver
{
    private readonly object _gate = new();
    private readonly List<MonitoringEvent> _events = new();

    public void OnMonitoringEvent(MonitoringEvent e)
    {
        lock (_gate)
            _events.Add(e);
    }

    public IReadOnlyList<MonitoringEvent> Events
    {
        get { lock (_gate) return _events.ToList(); }
    }

    public IReadOnlyList<HealthStateChange> StateChanges => Events
        .Where(e => e.StateChange is not null)
        .Select(e => e.StateChange!)
        .ToList();
}

/// <summary>Captures monitoring logger calls for lifecycle/secret-safety assertions.</summary>
public sealed class CapturingMonitoringLogger : IMonitoringLogger
{
    private readonly object _gate = new();
    private readonly List<string> _lines = new();

    public IReadOnlyList<string> Lines
    {
        get { lock (_gate) return _lines.ToList(); }
    }

    private void Add(FormattableString line)
    {
        lock (_gate)
            _lines.Add(line.ToString());
    }

    public void MonitoringStarted() => Add($"Started");
    public void MonitoringStopped() => Add($"Stopped");
    public void TargetAdded(string targetId) => Add($"TargetAdded:{targetId}");
    public void TargetRemoved(string targetId) => Add($"TargetRemoved:{targetId}");
    public void TargetEnabled(string targetId, bool enabled) => Add($"TargetEnabled:{targetId}:{enabled}");
    public void CheckScheduled(string checkId, string targetId) => Add($"CheckScheduled:{checkId}:{targetId}");
    public void HealthStateChanged(HealthStateChange change) => Add($"HealthStateChanged:{change.TargetId}:{change.Previous}->{change.New}");
    public void MonitoringError(string checkId, string message) => Add($"MonitoringError:{checkId}");
}

/// <summary>Builds fake tool registries + a real <see cref="ToolExecutionService"/> to exercise the check executor pipeline.</summary>
public static class MonitoringNetwork
{
    public static FakeNetworkTool Ping(Func<PingResult> factory) => new()
    {
        Name = "ping",
        InputType = typeof(PingInput),
        Timeout = TimeSpan.FromSeconds(30),
        Handler = (_, _, _) => Task.FromResult(ToolOutcome.Ok(factory())),
    };

    public static FakeNetworkTool PingFailure(string errorCode, string error) => new()
    {
        Name = "ping",
        InputType = typeof(PingInput),
        Timeout = TimeSpan.FromSeconds(30),
        Handler = (_, _, _) => Task.FromResult(ToolOutcome.Failed(errorCode, error)),
    };

    public static FakeNetworkTool Tcp(string state) => new()
    {
        Name = "tcp_test",
        InputType = typeof(TcpTestInput),
        Timeout = TimeSpan.FromSeconds(30),
        Handler = (input, _, _) =>
        {
            var t = (TcpTestInput)input!;
            return Task.FromResult(ToolOutcome.Ok(new TcpTestResult
            {
                Host = t.Host!,
                Port = t.Port,
                Reachable = state == "Open",
                State = state,
            }));
        },
    };

    public static FakeNetworkTool Dns(bool success) => new()
    {
        Name = "dns_lookup",
        InputType = typeof(DnsLookupInput),
        Timeout = TimeSpan.FromSeconds(30),
        Handler = (_, _, _) => success
            ? Task.FromResult(ToolOutcome.Ok(new DnsLookupResult
            {
                Host = "server01",
                Success = true,
                Records = new[] { new DnsRecord("server01", 60, "A", "10.0.0.20") },
            }))
            : Task.FromResult(ToolOutcome.Failed("DnsLookupFailed", "No DNS records returned.")),
    };

    public static ToolExecutionService Service(params INetworkTool[] tools)
    {
        var registry = new ToolRegistry();

        foreach (var tool in tools)
            registry.Register(tool);

        return new ToolExecutionService(registry);
    }
}