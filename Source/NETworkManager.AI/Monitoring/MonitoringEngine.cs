using NETworkManager.AI.Abstractions;

namespace NETworkManager.AI.Monitoring;

/// <summary>
///     The monitoring engine: manages the target/check lifecycle, delegates periodic execution to the bounded
///     scheduler, records results to the state store, evaluates health deterministically, and publishes
///     lifecycle/transition events. Monitoring is read-only at the check level — there is no remediation path.
/// </summary>
public sealed class MonitoringEngine : IMonitoringEngine
{
    private readonly MonitoringOptions _options;
    private readonly IMonitoringCheckExecutor _executor;
    private readonly IHealthEvaluator _evaluator;
    private readonly IMonitoringStateStore _store;
    private readonly IMonitoringLogger _logger;
    private readonly MonitoringScheduler _scheduler;

    private readonly object _gate = new();
    private readonly Dictionary<string, MonitoringTarget> _targets = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MonitoringCheck> _checks = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<IMonitoringObserver> _observers = new();

    private bool _started;

    public MonitoringEngine(
        MonitoringOptions options,
        IMonitoringCheckExecutor executor,
        IHealthEvaluator? evaluator = null,
        IMonitoringStateStore? store = null,
        IMonitoringLogger? logger = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _evaluator = evaluator ?? new HealthEvaluator();
        _store = store ?? new MonitoringStateStore();
        _logger = logger ?? new NullMonitoringLogger();

        _scheduler = new MonitoringScheduler(executor, OnResult, _logger, _options.MaxConcurrency, _options.DefaultInterval);
    }

    /// <summary>True while the periodic loop is running.</summary>
    public bool IsRunning
    {
        get
        {
            lock (_gate)
                return _started;
        }
    }

    // ----- lifecycle -------------------------------------------------------

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_started)
                return Task.CompletedTask;

            _started = true;
        }

        _logger.MonitoringStarted();
        Publish(new MonitoringEvent { Type = MonitoringEventType.MonitoringStarted, Timestamp = DateTimeOffset.UtcNow });
        _scheduler.Start(cancellationToken);

        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        lock (_gate)
        {
            if (!_started)
                return;

            _started = false;
        }

        await _scheduler.StopAsync().ConfigureAwait(false);

        _logger.MonitoringStopped();
        Publish(new MonitoringEvent { Type = MonitoringEventType.MonitoringStopped, Timestamp = DateTimeOffset.UtcNow });
    }

    // ----- target/check management -----------------------------------------

    public void AddTarget(MonitoringTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(target.Id);

        lock (_gate)
            _targets[target.Id] = target;

        _logger.TargetAdded(target.Id);
    }

    public bool RemoveTarget(string targetId)
    {
        List<string> removedCheckIds;

        lock (_gate)
        {
            if (!_targets.Remove(targetId))
                return false;

            removedCheckIds = _checks
                .Where(kv => string.Equals(kv.Value.TargetId, targetId, StringComparison.OrdinalIgnoreCase))
                .Select(kv => kv.Key)
                .ToList();

            foreach (var id in removedCheckIds)
                _checks.Remove(id);
        }

        foreach (var id in removedCheckIds)
            _scheduler.Remove(id);

        _store.RemoveTarget(targetId);
        _logger.TargetRemoved(targetId);

        return true;
    }

    public void SetTargetEnabled(string targetId, bool enabled)
    {
        lock (_gate)
        {
            if (!_targets.TryGetValue(targetId, out var target))
                throw new ArgumentException($"Target '{targetId}' is not registered.", nameof(targetId));

            _targets[targetId] = target with { Enabled = enabled };

            foreach (var check in _checks.Values.Where(c =>
                string.Equals(c.TargetId, targetId, StringComparison.OrdinalIgnoreCase)))
            {
                _scheduler.SetEnabled(check.CheckId, enabled && check.Enabled);
            }
        }

        _logger.TargetEnabled(targetId, enabled);
    }

    public void AddCheck(MonitoringCheck check)
    {
        ArgumentNullException.ThrowIfNull(check);
        ArgumentException.ThrowIfNullOrWhiteSpace(check.CheckId);

        MonitoringTarget target;

        lock (_gate)
        {
            if (!_targets.TryGetValue(check.TargetId, out target!))
                throw new ArgumentException($"Target '{check.TargetId}' is not registered.", nameof(check));

            _checks[check.CheckId] = check;
        }

        _scheduler.Add(check, target, check.Enabled && target.Enabled);
        _logger.CheckScheduled(check.CheckId, check.TargetId);
    }

    public bool RemoveCheck(string checkId)
    {
        lock (_gate)
        {
            if (!_checks.Remove(checkId))
                return false;
        }

        return _scheduler.Remove(checkId);
    }

    public void Clear()
    {
        List<string> checkIds;

        lock (_gate)
        {
            checkIds = _checks.Keys.ToList();
            _checks.Clear();
            _targets.Clear();
        }

        foreach (var checkId in checkIds)
            _scheduler.Remove(checkId);

        _store.Clear();
    }

    public async Task<MonitoringResult> RunCheckAsync(MonitoringCheck check, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(check);

        MonitoringTarget target;

        lock (_gate)
        {
            if (!_targets.TryGetValue(check.TargetId, out target!))
                throw new ArgumentException($"Target '{check.TargetId}' is not registered.", nameof(check));
        }

        var result = await _executor.ExecuteAsync(check, target, cancellationToken).ConfigureAwait(false);
        OnResult(check, target, result);

        return result;
    }

    // ----- query (read-only; the AI tool depends on this surface only) ------

    public IReadOnlyList<MonitoringSnapshot> GetCurrentStatus()
    {
        lock (_gate)
        {
            return _targets.Values
                .OrderBy(t => t.Id, StringComparer.OrdinalIgnoreCase)
                .Select(t =>
                {
                    var results = _store.GetLatestResults(t.Id);
                    var timestamp = results.Count == 0
                        ? DateTimeOffset.UtcNow
                        : results.Max(r => r.Timestamp);

                    return new MonitoringSnapshot
                    {
                        TargetId = t.Id,
                        DisplayName = t.DisplayName,
                        Health = _store.GetHealth(t.Id),
                        Timestamp = timestamp,
                        Results = results,
                    };
                })
                .ToList();
        }
    }

    public NetworkHealthStatus? GetHealth(string targetId)
    {
        lock (_gate)
            return _targets.ContainsKey(targetId) ? _store.GetHealth(targetId) : null;
    }

    public MonitoringResult? GetLatest(string targetId, MonitorCheckType type)
    {
        lock (_gate)
        {
            if (!_targets.ContainsKey(targetId))
                return null;
        }

        return _store.GetLatestResults(targetId).FirstOrDefault(r => r.CheckType == type);
    }

    public IReadOnlyList<MonitoringResult> GetRecentFailures(int maxCount) =>
        _store.GetRecentFailures(maxCount);

    // ----- observers -------------------------------------------------------

    public void Subscribe(IMonitoringObserver observer)
    {
        ArgumentNullException.ThrowIfNull(observer);

        lock (_gate)
            _observers.Add(observer);
    }

    public void Unsubscribe(IMonitoringObserver observer)
    {
        ArgumentNullException.ThrowIfNull(observer);

        lock (_gate)
            _observers.Remove(observer);
    }

    // ----- internal --------------------------------------------------------

    private void OnResult(MonitoringCheck check, MonitoringTarget target, MonitoringResult result)
    {
        _store.Record(result);

        var health = _evaluator.Evaluate(_store.GetLatestResults(target.Id));
        var previous = _store.GetHealth(target.Id);
        _store.SetHealth(target.Id, health, result.Timestamp);

        Publish(new MonitoringEvent
        {
            Type = MonitoringEventType.MonitoringCompleted,
            Timestamp = result.Timestamp,
            TargetId = target.Id,
            CheckId = check.CheckId,
            Message = result.SafeMessage,
            Result = result,
        });

        if (result.Status == MonitoringResultStatus.Error)
        {
            _logger.MonitoringError(check.CheckId, result.SafeMessage);
            Publish(new MonitoringEvent
            {
                Type = MonitoringEventType.MonitoringFailed,
                Timestamp = result.Timestamp,
                TargetId = target.Id,
                CheckId = check.CheckId,
                Message = result.SafeMessage,
                Result = result,
            });
        }

        if (health != previous)
        {
            var change = new HealthStateChange
            {
                TargetId = target.Id,
                DisplayName = target.DisplayName,
                Previous = previous,
                New = health,
                Timestamp = result.Timestamp,
                Reason = result.SafeMessage,
                RelatedResult = result,
            };

            _logger.HealthStateChanged(change);
            Publish(new MonitoringEvent
            {
                Type = MonitoringEventType.HealthStateChanged,
                Timestamp = result.Timestamp,
                TargetId = target.Id,
                StateChange = change,
            });
        }
    }

    private void Publish(MonitoringEvent e)
    {
        IMonitoringObserver[] snapshot;

        lock (_gate)
            snapshot = _observers.ToArray();

        foreach (var observer in snapshot)
        {
            try
            {
                observer.OnMonitoringEvent(e);
            }
            catch (Exception ex)
            {
                _logger.MonitoringError(e.CheckId ?? e.TargetId ?? string.Empty,
                    $"Monitoring observer threw: {ex.Message}");
            }
        }
    }
}