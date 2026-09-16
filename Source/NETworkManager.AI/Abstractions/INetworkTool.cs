using NETworkManager.AI.Models;

namespace NETworkManager.AI.Abstractions;

/// <summary>
///     Represents a single network tool that an AI system may invoke.
///     A tool is a strongly-typed, read-only (in this phase) operation that returns evidence.
/// </summary>
public interface INetworkTool
{
    /// <summary>Unique, stable, machine-friendly identifier (e.g. "ping").</summary>
    string Name { get; }

    /// <summary>Human-readable description of what the tool does.</summary>
    string Description { get; }

    /// <summary>Category used for grouping and discovery.</summary>
    ToolCategory Category { get; }

    /// <summary>Risk level of the operation (drives approval policy).</summary>
    ToolRiskLevel RiskLevel { get; }

    /// <summary>
    ///     Whether this tool requires explicit human approval before execution.
    ///     <c>false</c> for the read-only diagnostics introduced in this phase.
    /// </summary>
    bool RequiresApproval { get; }

    /// <summary>Default maximum duration for a single execution.</summary>
    TimeSpan Timeout { get; }

    /// <summary>The strongly-typed input model (must implement <see cref="IValidatableToolInput"/>).</summary>
    Type InputType { get; }

    /// <summary>The strongly-typed output model produced on success.</summary>
    Type OutputType { get; }

    /// <summary>
    ///     Executes the tool. Input is the typed input model (boxed). Returns an outcome describing success/error and data.
    ///     Must never throw for expected operational failures; unexpected exceptions are caught by the execution service.
    /// </summary>
    Task<ToolOutcome> ExecuteAsync(object? input, ToolExecutionContext context, CancellationToken cancellationToken);
}