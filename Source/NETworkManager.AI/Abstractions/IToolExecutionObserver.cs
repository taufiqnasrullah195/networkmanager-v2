using NETworkManager.AI.Models;

namespace NETworkManager.AI.Abstractions;

/// <summary>
///     Receives structured tool-execution activity (started/completed/failed/cancelled) so a UI can show live
///     progress. Implementations must treat the payloads as display-only: they carry safe metadata, never secrets.
/// </summary>
public interface IToolExecutionObserver
{
    void OnToolActivity(ToolActivity activity);
}