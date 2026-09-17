using NETworkManager.AI.Alerts;
using NETworkManager.AI.Monitoring;

namespace NETworkManager.AI.Abstractions;

/// <summary>Deterministic rule engine: maps a <see cref="HealthStateChange"/> to an <see cref="AlertDecision"/>.</summary>
public interface IAlertEvaluator
{
    AlertDecision Evaluate(HealthStateChange change);
}