using NETworkManager.AI.Abstractions;
using NETworkManager.AI.Models;

namespace NETworkManager.AI.Tests.Fakes;

/// <summary>Configurable <see cref="INetworkTool"/> for exercising the registry and execution service without a network.</summary>
public sealed class FakeNetworkTool : INetworkTool
{
    public required string Name { get; init; }
    public string Description { get; init; } = "Fake tool for tests.";
    public ToolCategory Category { get; init; } = ToolCategory.Connectivity;
    public ToolRiskLevel RiskLevel { get; init; } = ToolRiskLevel.Low;
    public bool RequiresApproval { get; init; }
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(5);
    public Type InputType { get; init; } = typeof(object);
    public Type OutputType { get; init; } = typeof(object);
    public Func<object?, ToolExecutionContext, CancellationToken, Task<ToolOutcome>>? Handler { get; init; }

    public Task<ToolOutcome> ExecuteAsync(object? input, ToolExecutionContext context, CancellationToken cancellationToken)
        => Handler?.Invoke(input, context, cancellationToken) ?? Task.FromResult(ToolOutcome.Ok());
}