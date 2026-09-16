using NETworkManager.AI.Abstractions;
using NETworkManager.AI.Models;

namespace NETworkManager.AI.Diagnostics;

/// <summary>Optional target configuration for the <c>internet_connectivity_diagnostic</c> tool. All fields optional.</summary>
public sealed record InternetConnectivityDiagnosticInput : IValidatableToolInput
{
    public string? Hostname { get; init; }
    public string? IPAddress { get; init; }
    public int? Port { get; init; }

    public IReadOnlyList<string> Validate() => Array.Empty<string>();

    public DiagnosticTarget ToTarget() => new() { Hostname = Hostname, IPAddress = IPAddress, Port = Port };
}

/// <summary>
///     Exposes the Internet Connectivity Diagnostic as a single read-only tool. A future Custom Agent requests
///     <c>internet_connectivity_diagnostic</c> with (optional) target arguments and receives a <see cref="DiagnosticReport"/>
///     — it never needs to know the internal steps.
/// </summary>
public sealed class InternetConnectivityDiagnosticTool : INetworkTool
{
    private readonly IDiagnosticEngine _engine;

    public InternetConnectivityDiagnosticTool(IDiagnosticEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    public string Name => "internet_connectivity_diagnostic";
    public string Description => "Runs a read-only internet connectivity diagnostic against the local network configuration.";
    public ToolCategory Category => ToolCategory.Connectivity;
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Low;
    public bool RequiresApproval => false;
    public TimeSpan Timeout => TimeSpan.FromMinutes(5);
    public Type InputType => typeof(InternetConnectivityDiagnosticInput);
    public Type OutputType => typeof(DiagnosticReport);

    public async Task<ToolOutcome> ExecuteAsync(object? input, ToolExecutionContext context, CancellationToken cancellationToken)
    {
        var args = input as InternetConnectivityDiagnosticInput ?? new InternetConnectivityDiagnosticInput();

        var report = await _engine
            .RunAsync(Workflows.InternetConnectivityDiagnostic.Create(), args.ToTarget(), cancellationToken)
            .ConfigureAwait(false);

        return ToolOutcome.Ok(report);
    }
}