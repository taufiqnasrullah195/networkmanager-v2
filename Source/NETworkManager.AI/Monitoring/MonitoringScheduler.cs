using NETworkManager.AI.Abstractions;

namespace NETworkManager.AI.Monitoring;

/// <summary>
///     Bounded periodic scheduler for monitoring checks. One consolidated loop (no task-per-target), a
///     <see cref="SemaphoreSlim"/> cap on concurrent checks, cancellation, and graceful shutdown.
/// </summary>
public sealed class MonitoringScheduler
{
    private readonly IMonitoringCheckExecutor _executor;
    private readonly Action<MonitoringCheck, MonitoringTarget, MonitoringResult> _resultHandler;
    private readonly IMonitoringLogger _logger;
    private readonly TimeSpan _defaultInterval;
    private readonly SemaphoreSlim _concurrency;

    private readonly object _gate = new();
    private readonly Dictionary<string, ScheduleEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

    private CancellationTokenSource? _cts;
    private Task? _loop;

    public MonitoringScheduler(
        IMonitoringCheckExecutor executor,
        Action<MonitoringCheck, MonitoringTarget, MonitoringResult> resultHandler,
        IMonitoringLogger logger,
        int maxConcurrency,
        TimeSpan defaultInterval)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _resultHandler = resultHandler ?? throw new ArgumentNullException(nameof(resultHandler));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _defaultInterval = defaultInterval;
        _concurrency = new SemaphoreSlim(maxConcurrency, maxConcurrency);
    }

    private sealed class ScheduleEntry(MonitoringCheck check, MonitoringTarget target, bool enabled)
    {
        public MonitoringCheck Check { get; } = check;
        public MonitoringTarget Target { get; } = target;
        public bool Enabled { get; set; } = enabled;
        public bool InFlight { get; set; }
        public DateTimeOffset NextDue { get; set; } = enabled ? DateTimeOffset.UtcNow : DateTimeOffset.MaxValue;
    }

    public void Add(MonitoringCheck check, MonitoringTarget target, bool enabled)
    {
        lock (_gate)
            _entries[check.CheckId] = new ScheduleEntry(check, target, enabled);
    }

    public bool Remove(string checkId)
    {
        lock (_gate)
            return _entries.Remove(checkId);
    }

    public void SetEnabled(string checkId, bool enabled)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(checkId, out var entry))
                return;

            entry.Enabled = enabled;
            entry.NextDue = enabled ? DateTimeOffset.UtcNow : DateTimeOffset.MaxValue;
        }
    }

    /// <summary>Starts the loop (returns immediately; the loop runs on a background task).</summary>
    public void Start(CancellationToken cancellationToken)
    {
        if (_loop is not null)
            return;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loop = Task.Run(() => RunAsync(_cts.Token));
    }

    /// <summary>Cancels and awaits the loop; in-flight checks observe cancellation and finish as Cancelled.</summary>
    public async Task StopAsync()
    {
        _cts?.Cancel();

        if (_loop is not null)
        {
            try
            {
                await _loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // expected on shutdown
            }
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            List<ScheduleEntry> due;
            TimeSpan? waitForNext = null;

            lock (_gate)
            {
                due = new List<ScheduleEntry>();
                var now = DateTimeOffset.UtcNow;

                foreach (var entry in _entries.Values)
                {
                    if (!entry.Enabled)
                        continue;

                    var remaining = entry.NextDue - now;

                    if (remaining <= TimeSpan.Zero)
                    {
                        if (!entry.InFlight)
                            due.Add(entry);
                    }
                    else if (waitForNext is null || remaining < waitForNext)
                    {
                        waitForNext = remaining;
                    }
                }
            }

            if (due.Count == 0)
            {
                // Idle: sleep until the next due entry (or a short quiet poll when nothing is scheduled).
                try
                {
                    await Task.Delay(waitForNext ?? TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                continue;
            }

            await Task.WhenAll(due.Select(e => RunEntryAsync(e, cancellationToken))).ConfigureAwait(false);
        }
    }

    private async Task RunEntryAsync(ScheduleEntry entry, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            entry.InFlight = true;
            entry.NextDue = DateTimeOffset.UtcNow + (entry.Check.Interval ?? _defaultInterval);
        }

        var acquired = false;

        try
        {
            await _concurrency.WaitAsync(cancellationToken).ConfigureAwait(false);
            acquired = true;

            var result = await _executor.ExecuteAsync(entry.Check, entry.Target, cancellationToken).ConfigureAwait(false);
            _resultHandler(entry.Check, entry.Target, result);
        }
        catch (OperationCanceledException)
        {
            // Cancelled while queued or mid-check — nothing completed to record.
        }
        catch (Exception ex)
        {
            var error = new MonitoringResult
            {
                CheckId = entry.Check.CheckId,
                TargetId = entry.Target.Id,
                CheckType = entry.Check.Type,
                Status = MonitoringResultStatus.Error,
                ErrorClassification = MonitorErrorClass.ExecutionError,
                Timestamp = DateTimeOffset.UtcNow,
                Duration = TimeSpan.Zero,
                SafeMessage = "Monitoring check failed unexpectedly.",
                CorrelationId = entry.Check.CheckId,
            };

            _logger.MonitoringError(entry.Check.CheckId, ex.Message);
            _resultHandler(entry.Check, entry.Target, error);
        }
        finally
        {
            if (acquired)
                _concurrency.Release();

            lock (_gate)
                entry.InFlight = false;
        }
    }
}