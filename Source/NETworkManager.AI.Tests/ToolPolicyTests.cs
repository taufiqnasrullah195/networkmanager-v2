using NETworkManager.AI.Models;
using NETworkManager.AI.Orchestration;
using NETworkManager.AI.Tests.Fakes;
using Xunit;

namespace NETworkManager.AI.Tests;

public class ToolPolicyTests
{
    private static FakeNetworkTool Tool(ToolRiskLevel risk) => new() { Name = "t", RiskLevel = risk };

    [Theory]
    [InlineData(ToolRiskLevel.Low, PolicyDecision.Allow)]
    [InlineData(ToolRiskLevel.Medium, PolicyDecision.RequireApproval)]
    [InlineData(ToolRiskLevel.High, PolicyDecision.RequireApproval)]
    [InlineData(ToolRiskLevel.Critical, PolicyDecision.Deny)]
    public void DefaultPolicy_maps_risk_to_decision(ToolRiskLevel risk, PolicyDecision expected)
    {
        var policy = new DefaultToolPolicyService();

        Assert.Equal(expected, policy.Evaluate(Tool(risk), new ToolExecutionContext()));
    }

    [Fact]
    public void Approval_is_not_required_when_policy_allows()
    {
        var approval = new DefaultToolApprovalService();

        Assert.Equal(ApprovalResult.NotRequired, approval.Decide(Tool(ToolRiskLevel.Low), new ToolExecutionContext(), PolicyDecision.Allow));
    }

    [Fact]
    public void Approval_is_required_without_grant()
    {
        var approval = new DefaultToolApprovalService();

        Assert.Equal(ApprovalResult.Required, approval.Decide(Tool(ToolRiskLevel.High), new ToolExecutionContext(), PolicyDecision.RequireApproval));
    }

    [Fact]
    public void Approval_is_granted_with_approval_context()
    {
        var approval = new DefaultToolApprovalService();

        Assert.Equal(ApprovalResult.Approved, approval.Decide(Tool(ToolRiskLevel.High), new ToolExecutionContext { ApprovalGranted = true }, PolicyDecision.RequireApproval));
    }
}