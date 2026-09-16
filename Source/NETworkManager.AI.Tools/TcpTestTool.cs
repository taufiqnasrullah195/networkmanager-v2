using System.Net.Sockets;
using NETworkManager.AI.Abstractions;
using NETworkManager.AI.Models;

namespace NETworkManager.AI.Tools;

/// <summary>
///     <c>tcp_test</c> tool. Uses a single <see cref="TcpClient"/> connect for a host:port probe.
///     NETworkManager's <c>PortProbe</c> helper is internal to the Models assembly and <c>PortScanner</c> targets many
///     ports, so a single TCP connect is implemented directly for this one-shot test.
/// </summary>
public sealed class TcpTestTool : INetworkTool
{
    public string Name => "tcp_test";
    public string Description => "Tests TCP connectivity to a host:port (open / closed / timed out).";
    public ToolCategory Category => ToolCategory.Connectivity;
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Low;
    public bool RequiresApproval => false;
    public TimeSpan Timeout => TimeSpan.FromSeconds(30);
    public Type InputType => typeof(TcpTestInput);
    public Type OutputType => typeof(TcpTestResult);

    public async Task<ToolOutcome> ExecuteAsync(object? input, ToolExecutionContext context, CancellationToken cancellationToken)
    {
        var tcpInput = (TcpTestInput)input!;

        using var client = new TcpClient();
        using var timeoutCts = new CancellationTokenSource(tcpInput.TimeoutMilliseconds);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        string state;
        bool reachable;

        try
        {
            await client.ConnectAsync(tcpInput.Host!, tcpInput.Port, linkedCts.Token).ConfigureAwait(false);
            reachable = true;
            state = "Open";
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            reachable = false;
            state = "TimedOut";
        }
        catch (OperationCanceledException)
        {
            return ToolOutcome.Failed("Cancelled", "TCP connectivity test was cancelled.");
        }
        catch (Exception)
        {
            reachable = false;
            state = "Closed";
        }

        return ToolOutcome.Ok(new TcpTestResult
        {
            Host = tcpInput.Host!,
            Port = tcpInput.Port,
            Reachable = reachable,
            State = state,
        });
    }
}