using NETworkManager.AI.Abstractions;
using NETworkManager.AI.Models;

namespace NETworkManager.AI.Orchestration;

/// <summary>Default approval resolution: only tools whose policy requires approval need it; approval is read from the context.</summary>
public sealed class DefaultToolApprovalService : IToolApprovalService
{
    public ApprovalResult Decide(INetworkTool tool, ToolExecutionContext context, PolicyDecision policy)
    {
        if (policy != PolicyDecision.RequireApproval)
            return ApprovalResult.NotRequired;

        return context.ApprovalGranted == true ? ApprovalResult.Approved : ApprovalResult.Required;
    }
}