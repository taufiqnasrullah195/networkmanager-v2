using NETworkManager.AI.Alerts;
using NETworkManager.AI.Monitoring;

namespace NETworkManager.AI.Abstractions;

/// <summary>
///     Consumes monitoring state changes and produces/updates/resolves structured alerts. Read-only toward the
///     network; publishes lifecycle events; supports start/stop (shutdown ignores further input). No AI calls.
/// </summary>
public interface IAlertEngine : IAlertQuery
{
    void Start();

    void Stop();

    bool IsRunning { get; }

    /// <summary>Evaluates a health-state transition and creates/updates/resolves alerts accordingly.</summary>
    void ProcessHealthStateChanged(HealthStateChange change);

    /// <summary>Updates the occurrence metadata of an active alert for a recurring (non-transition) failure.</summary>
    void ProcessRecurringFailure(MonitoringResult result);

    bool AcknowledgeAlert(string alertId);

    bool ResolveAlert(string alertId, string? resolutionEvidence = null);

    void Subscribe(IAlertObserver observer);

    void Unsubscribe(IAlertObserver observer);
}