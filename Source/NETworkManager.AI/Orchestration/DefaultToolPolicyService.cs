using NETworkManager.AI.Abstractions;
using NETworkManager.AI.Models;

namespace NETworkManager.AI.Orchestration;

/// <summary>Safe default policy: LOW read-only diagnostics ALLOW, MEDIUM/HIGH REQUIRE_APPROVAL, CRITICAL DENY.</summary>
public sealed class DefaultToolPolicyService : IToolPolicyService
{
    public PolicyDecision Evaluate(INetworkTool tool, ToolExecutionContext context) => tool.RiskLevel switch
    {
        ToolRiskLevel.Low => PolicyDecision.Allow,
        ToolRiskLevel.Medium or ToolRiskLevel.High => PolicyDecision.RequireApproval,
        ToolRiskLevel.Critical => PolicyDecision.Deny,
        _ => PolicyDecision.Deny,
    };
}