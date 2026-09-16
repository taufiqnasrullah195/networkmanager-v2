using NETworkManager.AI.Models;

namespace NETworkManager.AI.Abstractions;

/// <summary>Approval boundary: given a policy decision and execution context, resolves whether a tool is approved to run.</summary>
public interface IToolApprovalService
{
    ApprovalResult Decide(INetworkTool tool, ToolExecutionContext context, PolicyDecision policy);
}