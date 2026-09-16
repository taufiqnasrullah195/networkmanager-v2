namespace NETworkManager.AI.Diagnostics;

/// <summary>Structured, secret-free logging hook for diagnostic lifecycle events. Sensitive values are never logged.</summary>
public interface IDiagnosticLogger
{
    void Log(DiagnosticLogEvent @event, IReadOnlyDictionary<string, object?>? data = null);
}

/// <summary>No-op logger — the default when no logger is configured.</summary>
public sealed class NullDiagnosticLogger : IDiagnosticLogger
{
    public static NullDiagnosticLogger Instance { get; } = new();

    public void Log(DiagnosticLogEvent @event, IReadOnlyDictionary<string, object?>? data = null)
    {
    }
}