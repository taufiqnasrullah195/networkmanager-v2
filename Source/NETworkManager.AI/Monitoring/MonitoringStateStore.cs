using NETworkManager.AI.Abstractions;

namespace NETworkManager.AI.Monitoring;

/// <summary>
///     Thread-safe in-memory monitoring state. Keeps the latest result per target/check-type, per-target health,
///     and a bounded buffer of recent failures. A future durable store can replace this behind
///     <see cref="IMonitoringStateStore"/> without touching the engine.
/// </summary>
public sealed class MonitoringStateStore : IMonitoringStateStore
{
    private const int FailureBufferLimit = 1000;

    private readonly object _gate = new();

    private readonly Dictionary<string, Dictionary<MonitorCheckType, MonitoringResult>> _latest = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, NetworkHealthStatus> _health = new(StringComparer.OrdinalIgnoreCase);

    private readonly List<MonitoringResult> _recentFailures = new();

    public void Record(MonitoringResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        lock (_gate)
        {
            if (!_latest.TryGetValue(result.TargetId, out var byType))
            {
                byType = new Dictionary<MonitorCheckType, MonitoringResult>();
                _latest[result.TargetId] = byType;
            }

            byType[result.CheckType] = result;

            if (result.Status is MonitoringResultStatus.Unhealthy
                or MonitoringResultStatus.Timeout
                or MonitoringResultStatus.Error)
            {
                _recentFailures.Add(result);
                if (_recentFailures.Count > FailureBufferLimit)
                    _recentFailures.RemoveAt(0);
            }
        }
    }

    public void SetHealth(string targetId, NetworkHealthStatus health, DateTimeOffset timestamp)
    {
        lock (_gate)
            _health[targetId] = health;
    }

    public NetworkHealthStatus GetHealth(string targetId)
    {
        lock (_gate)
            return _health.TryGetValue(targetId, out var health) ? health : NetworkHealthStatus.Unknown;
    }

    public IReadOnlyList<MonitoringResult> GetLatestResults(string targetId)
    {
        lock (_gate)
        {
            if (!_latest.TryGetValue(targetId, out var byType))
                return Array.Empty<MonitoringResult>();

            return byType.Values.OrderBy(r => r.CheckType).ToList();
        }
    }

    public IReadOnlyList<MonitoringResult> GetRecentFailures(int maxCount)
    {
        lock (_gate)
        {
            var count = Math.Min(Math.Max(maxCount, 0), _recentFailures.Count);
            return Enumerable.Range(0, count)
                .Select(i => _recentFailures[_recentFailures.Count - 1 - i])
                .ToList();
        }
    }

    public void RemoveTarget(string targetId)
    {
        lock (_gate)
        {
            _latest.Remove(targetId);
            _health.Remove(targetId);
            _recentFailures.RemoveAll(r => string.Equals(r.TargetId, targetId, StringComparison.OrdinalIgnoreCase));
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _latest.Clear();
            _health.Clear();
            _recentFailures.Clear();
        }
    }
}