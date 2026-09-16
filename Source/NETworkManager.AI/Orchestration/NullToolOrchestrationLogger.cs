using NETworkManager.AI.Abstractions;
using NETworkManager.AI.Models;

namespace NETworkManager.AI.Orchestration;

/// <summary>No-op logger — the default when no logger is configured.</summary>
public sealed class NullToolOrchestrationLogger : IToolOrchestrationLogger
{
    public static NullToolOrchestrationLogger Instance { get; } = new();

    public void Log(ToolOrchestrationEvent @event, IReadOnlyDictionary<string, object?>? data = null)
    {
    }
}