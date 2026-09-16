using NETworkManager.AI.Abstractions;
using NETworkManager.AI.Models;

namespace NETworkManager.AI.Tests.Fakes;

public sealed record EchoInput : IValidatableToolInput
{
    public string? Message { get; init; }

    public IReadOnlyList<string> Validate()
    {
        if (string.IsNullOrWhiteSpace(Message))
            return new[] { "Message must not be empty." };

        return Array.Empty<string>();
    }
}

public sealed record EchoOutput(string Message);

/// <summary>Deterministic test tool (<c>echo_test</c>) — echoes its input message. Test-only; not a production network tool.</summary>
public sealed class EchoTestTool : INetworkTool
{
    public string Name => "echo_test";
    public string Description => "Echoes the provided message back (test tool).";
    public ToolCategory Category => ToolCategory.Connectivity;
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Low;
    public bool RequiresApproval => false;
    public TimeSpan Timeout => TimeSpan.FromSeconds(1);
    public Type InputType => typeof(EchoInput);
    public Type OutputType => typeof(EchoOutput);

    public Task<ToolOutcome> ExecuteAsync(object? input, ToolExecutionContext context, CancellationToken cancellationToken)
    {
        var echoInput = (EchoInput)input!;
        return Task.FromResult(ToolOutcome.Ok(new EchoOutput(echoInput.Message ?? string.Empty)));
    }
}