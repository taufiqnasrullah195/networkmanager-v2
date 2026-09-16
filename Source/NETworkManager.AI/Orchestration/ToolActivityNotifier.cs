using NETworkManager.AI.Abstractions;
using NETworkManager.AI.Diagnostics;
using NETworkManager.AI.Models;

namespace NETworkManager.AI.Orchestration;

/// <summary>
///     Bridges the existing structured logging events (orchestrator + diagnostic engine) to an observer/event stream
///     the UI can bind to. Implements both logger interfaces so it plugs into the Step 5/6 graph without changing
///     any signature, and raises <see cref="Activity"/> as <see cref="ToolActivity"/> records built from
///     safe metadata only. Events may be raised on background threads — UI subscribers must marshal.
/// </summary>
public sealed class ToolActivityNotifier : IToolOrchestrationLogger, IDiagnosticLogger, IToolExecutionObserver
{
    private readonly object _lock = new();

    /// <summary>Raised for every tool-execution activity (started/completed/failed/cancelled). Background threads possible.</summary>
    public event EventHandler<ToolActivity>? Activity;

    public void OnToolActivity(ToolActivity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        Raise(activity);
    }

    public void Log(ToolOrchestrationEvent @event, IReadOnlyDictionary<string, object?>? data = null)
    {
        var d = data ?? new Dictionary<string, object?>();

        switch (@event)
        {
            case ToolOrchestrationEvent.ToolExecutionStarted:
                Raise(new ToolActivity
                {
                    ToolName = String(d, "toolName") ?? "?",
                    Category = String(d, "category"),
                    Status = ToolActivityStatus.Started,
                });
                break;

            case ToolOrchestrationEvent.ToolExecutionCompleted:
                Raise(new ToolActivity
                {
                    ToolName = String(d, "toolName") ?? "?",
                    Category = String(d, "category"),
                    Status = ToolActivityStatus.Completed,
                    Duration = Ms(d, "durationMs"),
                });
                break;

            case ToolOrchestrationEvent.ToolExecutionFailed:
                Raise(new ToolActivity
                {
                    ToolName = String(d, "toolName") ?? "?",
                    Category = String(d, "category"),
                    Status = ToolActivityStatus.Failed,
                    Duration = Ms(d, "durationMs"),
                    Summary = String(d, "errorCode"),
                });
                break;

            case ToolOrchestrationEvent.ToolCallCancelled:
                Raise(new ToolActivity
                {
                    ToolName = String(d, "toolName") ?? "?",
                    Status = ToolActivityStatus.Cancelled,
                });
                break;
        }
    }

    public void Log(DiagnosticLogEvent @event, IReadOnlyDictionary<string, object?>? data = null)
    {
        var d = data ?? new Dictionary<string, object?>();

        switch (@event)
        {
            case DiagnosticLogEvent.DiagnosticStepStarted:
                Raise(new ToolActivity
                {
                    ToolName = String(d, "toolName") ?? String(d, "stepId") ?? "?",
                    StepId = String(d, "stepId"),
                    Status = ToolActivityStatus.Started,
                });
                break;

            case DiagnosticLogEvent.DiagnosticStepCompleted:
                Raise(new ToolActivity
                {
                    ToolName = String(d, "toolName") ?? String(d, "stepId") ?? "?",
                    StepId = String(d, "stepId"),
                    Status = ToolActivityStatus.Completed,
                    Duration = Ms(d, "durationMs"),
                    Summary = String(d, "status") is { } s && s != "Passed" ? s.ToLowerInvariant() : null,
                });
                break;

            case DiagnosticLogEvent.DiagnosticStepFailed:
                Raise(new ToolActivity
                {
                    ToolName = String(d, "toolName") ?? String(d, "stepId") ?? "?",
                    StepId = String(d, "stepId"),
                    Status = ToolActivityStatus.Failed,
                    Summary = String(d, "errorCode") ?? String(d, "detail"),
                });
                break;

            case DiagnosticLogEvent.DiagnosticStepSkipped:
                Raise(new ToolActivity
                {
                    ToolName = String(d, "toolName") ?? String(d, "stepId") ?? "?",
                    StepId = String(d, "stepId"),
                    Status = ToolActivityStatus.Completed,
                    Summary = "skipped",
                });
                break;

            case DiagnosticLogEvent.DiagnosticCancelled:
                Raise(new ToolActivity
                {
                    ToolName = String(d, "diagnosticId") ?? "diagnostic",
                    Status = ToolActivityStatus.Cancelled,
                });
                break;
        }
    }

    private void Raise(ToolActivity activity)
    {
        EventHandler<ToolActivity>? handlers;
        lock (_lock)
            handlers = Activity;

        handlers?.Invoke(this, activity);
    }

    private static string? String(IReadOnlyDictionary<string, object?> data, string key)
        => data.TryGetValue(key, out var value) ? value?.ToString() : null;

    private static TimeSpan? Ms(IReadOnlyDictionary<string, object?> data, string key)
    {
        if (!data.TryGetValue(key, out var value) || value is null)
            return null;

        return double.TryParse(value.ToString(), out var ms) ? TimeSpan.FromMilliseconds(ms) : null;
    }
}