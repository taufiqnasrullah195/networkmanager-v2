using NETworkManager.AI.Abstractions;
using NETworkManager.AI.Models;

namespace NETworkManager.AI.Orchestration;

/// <summary>
///     Controlled bridge between an AI provider's tool call and TheWiseNetwork's tools:
///     validate structure → resolve from registry (never create dynamically) → policy → approval → execute through
///     <see cref="IToolExecutionService"/>. Returns a structured <see cref="ToolCallOutcome"/> carrying the audit
///     picture and the execution evidence. It never performs network work itself and never invokes a shell.
/// </summary>
public sealed class ToolCallOrchestrator : IToolCallOrchestrator
{
    private readonly IToolRegistry _registry;
    private readonly IToolExecutionService _executionService;
    private readonly IToolPolicyService _policy;
    private readonly IToolApprovalService _approval;
    private readonly IToolOrchestrationLogger _logger;

    public ToolCallOrchestrator(
        IToolRegistry registry,
        IToolExecutionService executionService,
        IToolPolicyService? policy = null,
        IToolApprovalService? approval = null,
        IToolOrchestrationLogger? logger = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _executionService = executionService ?? throw new ArgumentNullException(nameof(executionService));
        _policy = policy ?? new DefaultToolPolicyService();
        _approval = approval ?? new DefaultToolApprovalService();
        _logger = logger ?? NullToolOrchestrationLogger.Instance;
    }

    public async Task<ToolCallOutcome> ExecuteAsync(AIToolCall toolCall, ToolExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(toolCall);
        ArgumentNullException.ThrowIfNull(context);

        var timestamp = DateTimeOffset.UtcNow;

        // 0. Cancellation — never dispatch once the caller has cancelled.
        if (cancellationToken.IsCancellationRequested)
            return Fail(toolCall, timestamp, null, "Cancelled", "Tool call was cancelled before dispatch.");

        _logger.Log(ToolOrchestrationEvent.ToolCallReceived, new Dictionary<string, object?> { ["callId"] = toolCall.CallId, ["toolName"] = toolCall.ToolName });

        // 1. Structure validation
        if (string.IsNullOrWhiteSpace(toolCall.CallId))
            return Fail(toolCall, timestamp, null, "InvalidCall", "Tool call is missing a CallId.");

        if (string.IsNullOrWhiteSpace(toolCall.ToolName))
            return Fail(toolCall, timestamp, null, "InvalidCall", "Tool call is missing a ToolName.");

        // 2. Resolve from the registered allowlist — never create tools dynamically.
        if (!_registry.TryGet(toolCall.ToolName, out var tool) || tool is null)
        {
            _logger.Log(ToolOrchestrationEvent.ToolValidationFailed, new Dictionary<string, object?> { ["toolName"] = toolCall.ToolName });
            return Fail(toolCall, timestamp, null, "ToolNotFound", $"No tool registered with the name '{toolCall.ToolName}'.");
        }

        _logger.Log(ToolOrchestrationEvent.ToolResolved, new Dictionary<string, object?> { ["toolName"] = tool.Name });

        // 3. Policy
        var decision = _policy.Evaluate(tool, context);

        if (decision == PolicyDecision.Deny)
        {
            _logger.Log(ToolOrchestrationEvent.PolicyDenied, new Dictionary<string, object?> { ["toolName"] = tool.Name });
            return Fail(toolCall, timestamp, decision, "PolicyDenied", $"Policy denies execution of tool '{tool.Name}'.", null);
        }

        // 4. Approval
        var approval = _approval.Decide(tool, context, decision);

        if (decision == PolicyDecision.RequireApproval && approval != ApprovalResult.Approved)
        {
            _logger.Log(ToolOrchestrationEvent.ApprovalRequired, new Dictionary<string, object?> { ["toolName"] = tool.Name });
            return Fail(toolCall, timestamp, decision, "ApprovalRequired", $"Tool '{tool.Name}' requires approval.", approval);
        }

        // 5. Execute through the execution service (JSON args → typed input → validation → timeout/cancellation → tool).
        var executionContext = decision == PolicyDecision.RequireApproval
            ? context with { ToolCallId = toolCall.CallId, ApprovalGranted = true }
            : context with { ToolCallId = toolCall.CallId };

        _logger.Log(ToolOrchestrationEvent.ToolExecutionStarted, new Dictionary<string, object?> { ["toolName"] = tool.Name, ["callId"] = toolCall.CallId });

        var result = await _executionService.ExecuteAsync(toolCall, executionContext, cancellationToken).ConfigureAwait(false);

        _logger.Log(result.Success ? ToolOrchestrationEvent.ToolExecutionCompleted : ToolOrchestrationEvent.ToolExecutionFailed,
            new Dictionary<string, object?> { ["toolName"] = tool.Name, ["errorCode"] = result.ErrorCode });

        return new ToolCallOutcome
        {
            CallId = toolCall.CallId,
            ToolName = tool.Name,
            Executed = true,
            PolicyDecision = decision,
            ApprovalResult = approval,
            ToolResult = result,
            Timestamp = timestamp,
            Duration = DateTimeOffset.UtcNow - timestamp,
        };
    }

    public async Task<IReadOnlyList<ToolCallOutcome>> ExecuteAsync(IReadOnlyList<AIToolCall> toolCalls,
        ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(toolCalls);

        var outcomes = new List<ToolCallOutcome>(toolCalls.Count);

        foreach (var toolCall in toolCalls)
        {
            if (cancellationToken.IsCancellationRequested)
                break; // stop scheduling further tools once cancelled

            outcomes.Add(await ExecuteAsync(toolCall, context, cancellationToken).ConfigureAwait(false));
        }

        return outcomes;
    }

    private ToolCallOutcome Fail(AIToolCall call, DateTimeOffset timestamp, PolicyDecision? decision, string errorCode,
        string error, ApprovalResult? approval = null) => new()
    {
        CallId = call.CallId ?? string.Empty,
        ToolName = call.ToolName ?? string.Empty,
        Executed = false,
        PolicyDecision = decision,
        ApprovalResult = approval,
        ErrorCode = errorCode,
        Error = error,
        Timestamp = timestamp,
        Duration = DateTimeOffset.UtcNow - timestamp,
    };
}