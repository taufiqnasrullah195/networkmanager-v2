using NETworkManager.AI.Monitoring;

namespace NETworkManager.AI.Abstractions;

/// <summary>
///     Future-compatible suppression boundary (maintenance windows, muted targets, severity thresholds). This step
///     ships a no-op policy; real suppression lands later without touching the engine.
/// </summary>
public interface IAlertSuppressionPolicy
{
    bool IsSuppressed(HealthStateChange change);
}