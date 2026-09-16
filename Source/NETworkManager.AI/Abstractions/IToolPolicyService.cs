using NETworkManager.AI.Models;

namespace NETworkManager.AI.Abstractions;

/// <summary>Provider-neutral policy boundary: decides whether a tool may run given its metadata and the execution context.</summary>
public interface IToolPolicyService
{
    PolicyDecision Evaluate(INetworkTool tool, ToolExecutionContext context);
}