using NETworkManager.AI.Models;

namespace NETworkManager.AI.Abstractions;

/// <summary>Structured, secret-free logging hook for tool orchestration events. Tool argument values and error bodies are never logged.</summary>
public interface IToolOrchestrationLogger
{
    void Log(ToolOrchestrationEvent @event, IReadOnlyDictionary<string, object?>? data = null);
}