namespace NETworkManager.AI.Diagnostics;

/// <summary>
///     Coordinates diagnostic workflows: runs steps through the tool execution service, collects structured evidence,
///     distinguishes failed vs skipped checks, and produces a <see cref="DiagnosticReport"/>. It never performs
///     networking itself and never calls an AI provider — it reuses the tool registry/execution boundary.
/// </summary>
public interface IDiagnosticEngine
{
    Task<DiagnosticReport> RunAsync(DiagnosticWorkflow workflow, DiagnosticTarget target, CancellationToken cancellationToken = default);
}