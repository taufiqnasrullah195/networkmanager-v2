using NETworkManager.AI.Abstractions;
using NETworkManager.AI.Models;

namespace NETworkManager.AI.Diagnostics;

/// <summary>
///     Runs a diagnostic workflow sequentially: resolves each step's tool via the execution service, collects
///     structured evidence, distinguishes FAILED vs SKIPPED checks, enforces the workflow timeout, and produces a
///     <see cref="DiagnosticReport"/>. Read-only by construction — it only reuses registered tools.
/// </summary>
public sealed class DiagnosticEngine : IDiagnosticEngine
{
    private readonly IToolExecutionService _executionService;
    private readonly IDiagnosticLogger _logger;

    public DiagnosticEngine(IToolExecutionService executionService, IDiagnosticLogger? logger = null)
    {
        _executionService = executionService ?? throw new ArgumentNullException(nameof(executionService));
        _logger = logger ?? NullDiagnosticLogger.Instance;
    }

    public async Task<DiagnosticReport> RunAsync(DiagnosticWorkflow workflow, DiagnosticTarget target,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(target);

        var errors = workflow.Validate();
        if (errors.Count > 0)
            throw new ArgumentException($"Invalid diagnostic workflow: {string.Join(" ", errors)}", nameof(workflow));

        var diagnosticId = Guid.NewGuid().ToString("N");
        var startedAt = DateTimeOffset.UtcNow;
        var results = new List<DiagnosticStepResult>(workflow.Steps.Count);
        var cancelled = false;

        _logger.Log(DiagnosticLogEvent.DiagnosticStarted, new Dictionary<string, object?> { ["diagnosticId"] = diagnosticId, ["workflow"] = workflow.Name });

        using var workflowCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        workflowCts.CancelAfter(workflow.Timeout);

        foreach (var step in workflow.Steps)
        {
            if (workflowCts.IsCancellationRequested)
            {
                cancelled = true;
                results.Add(new DiagnosticStepResult { StepId = step.Id, Status = CheckStatus.Cancelled, Required = step.Required, Classification = step.ClassifiesAs });
                continue;
            }

            // Dependency gate: a step runs only if every dependency PASSED; otherwise it is SKIPPED (not failed).
            var unmet = step.DependsOn
                .Where(id => results.FirstOrDefault(r => r.StepId == id)?.Status != CheckStatus.Passed)
                .ToList();

            if (unmet.Count > 0)
            {
                _logger.Log(DiagnosticLogEvent.DiagnosticStepSkipped, new Dictionary<string, object?> { ["stepId"] = step.Id, ["blockedBy"] = string.Join(",", unmet) });
                results.Add(new DiagnosticStepResult
                {
                    StepId = step.Id,
                    Status = CheckStatus.Skipped,
                    Required = step.Required,
                    Classification = step.ClassifiesAs,
                    SkipReason = $"Blocked by previous failure: {string.Join(", ", unmet)}.",
                });
                continue;
            }

            _logger.Log(DiagnosticLogEvent.DiagnosticStepStarted, new Dictionary<string, object?> { ["stepId"] = step.Id });
            results.Add(await ExecuteStepAsync(step, target, results, workflowCts.Token).ConfigureAwait(false));
        }

        var completedAt = DateTimeOffset.UtcNow;
        var status = ComputeStatus(results, cancelled);

        if (cancelled)
            _logger.Log(DiagnosticLogEvent.DiagnosticCancelled, new Dictionary<string, object?> { ["diagnosticId"] = diagnosticId });
        else
            _logger.Log(DiagnosticLogEvent.DiagnosticCompleted, new Dictionary<string, object?> { ["diagnosticId"] = diagnosticId, ["status"] = status.ToString() });

        return new DiagnosticReport
        {
            DiagnosticId = diagnosticId,
            Name = workflow.Name,
            Target = target,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            Status = status,
            Steps = results,
            Summary = BuildSummary(workflow, status, results),
        };
    }

    private async Task<DiagnosticStepResult> ExecuteStepAsync(DiagnosticStep step, DiagnosticTarget target,
        IReadOnlyList<DiagnosticStepResult> completed, CancellationToken token)
    {
        var context = new DiagnosticStepContext { Target = target, Completed = completed };

        string argumentsJson;
        try
        {
            argumentsJson = step.Arguments?.Invoke(context) ?? step.ArgumentsJson ?? "{}";
        }
        catch (Exception ex)
        {
            return Fail(step, null, "ArgumentResolutionFailed", ex.Message);
        }

        var call = new AIToolCall { CallId = step.Id, ToolName = step.ToolName, ArgumentsJson = argumentsJson };

        ToolResult result;
        try
        {
            result = await _executionService.ExecuteAsync(call, new ToolExecutionContext { ToolCallId = step.Id }, token)
                .WaitAsync(step.Timeout, token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return new DiagnosticStepResult { StepId = step.Id, Status = CheckStatus.Cancelled, Required = step.Required, Classification = step.ClassifiesAs };
        }
        catch (TimeoutException)
        {
            return Fail(step, null, "TimedOut", $"Step '{step.Id}' exceeded its timeout of {step.Timeout.TotalSeconds:0.#}s.");
        }

        if (!result.Success)
        {
            _logger.Log(DiagnosticLogEvent.DiagnosticStepFailed, new Dictionary<string, object?> { ["stepId"] = step.Id, ["errorCode"] = result.ErrorCode });
            return Fail(step, result.Data, result.ErrorCode ?? "ExecutionError", result.Error ?? $"Tool '{step.ToolName}' failed.");
        }

        var evaluation = step.Evaluator?.Invoke(result.Data, context) ?? StepEvaluation.Pass();

        CheckStatus status;
        if (!evaluation.Passed)
        {
            status = CheckStatus.Failed;
            _logger.Log(DiagnosticLogEvent.DiagnosticStepFailed, new Dictionary<string, object?> { ["stepId"] = step.Id, ["detail"] = evaluation.Detail });
        }
        else if (evaluation.Warning)
        {
            status = CheckStatus.Warning;
        }
        else
        {
            status = CheckStatus.Passed;
        }

        _logger.Log(DiagnosticLogEvent.DiagnosticStepCompleted, new Dictionary<string, object?> { ["stepId"] = step.Id, ["status"] = status.ToString() });

        return new DiagnosticStepResult
        {
            StepId = step.Id,
            Status = status,
            Required = step.Required,
            Classification = step.ClassifiesAs,
            Evidence = new DiagnosticEvidence
            {
                StepId = step.Id,
                Tool = step.ToolName,
                Target = EvidenceTarget(result.Data),
                Success = status is CheckStatus.Passed or CheckStatus.Warning,
                Data = result.Data,
                Error = evaluation.Detail,
                ErrorCode = status == CheckStatus.Failed ? "CheckFailed" : null,
                Timestamp = result.Timestamp,
                Duration = result.Duration,
            },
        };
    }

    private static DiagnosticStepResult Fail(DiagnosticStep step, object? data, string errorCode, string error) =>
        new()
        {
            StepId = step.Id,
            Status = CheckStatus.Failed,
            Required = step.Required,
            Classification = step.ClassifiesAs,
            Evidence = new DiagnosticEvidence
            {
                StepId = step.Id,
                Tool = step.ToolName,
                Success = false,
                Data = data,
                Error = error,
                ErrorCode = errorCode,
                Timestamp = DateTimeOffset.UtcNow,
                Duration = TimeSpan.Zero,
            },
        };

    private static DiagnosticStatus ComputeStatus(IReadOnlyList<DiagnosticStepResult> results, bool cancelled)
    {
        if (cancelled)
            return DiagnosticStatus.Cancelled;

        if (results.Any(r => r.Required && r.Status == CheckStatus.Failed))
            return DiagnosticStatus.Failed;

        if (results.Any(r => r.Status is CheckStatus.Failed or CheckStatus.Warning))
            return DiagnosticStatus.Warning;

        return DiagnosticStatus.Success;
    }

    private static string BuildSummary(DiagnosticWorkflow workflow, DiagnosticStatus status, IReadOnlyList<DiagnosticStepResult> results)
    {
        var passed = results.Count(r => r.Status is CheckStatus.Passed or CheckStatus.Warning);
        var failed = results.Count(r => r.Status == CheckStatus.Failed);
        var skipped = results.Count(r => r.Status == CheckStatus.Skipped);

        return $"{workflow.Name} finished with status {status}: {passed} passed, {failed} failed, {skipped} skipped.";
    }

    private static string? EvidenceTarget(object? data) => data switch
    {
        PingResult p => p.Target,
        TcpTestResult t => $"{t.Host}:{t.Port}",
        DnsLookupResult d => d.Host,
        TracerouteResult t => t.Target,
        _ => null,
    };
}