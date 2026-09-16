namespace NETworkManager.AI.Diagnostics;

/// <summary>Target of a diagnostic. Only the fields a workflow needs are set.</summary>
public sealed record DiagnosticTarget
{
    public string? Hostname { get; init; }
    public string? IPAddress { get; init; }
    public int? Port { get; init; }
    public string? Interface { get; init; }
    public string? Network { get; init; }

    public static DiagnosticTarget Empty { get; } = new();
}

/// <summary>Structured, ordered evidence of one executed check — what was actually observed.</summary>
public sealed record DiagnosticEvidence
{
    /// <summary>Diagnostic step that produced this evidence (the "source").</summary>
    public required string StepId { get; init; }

    /// <summary>Underlying tool name that collected the observation.</summary>
    public required string Tool { get; init; }

    public string? Target { get; init; }
    public required bool Success { get; init; }
    public object? Data { get; init; }
    public string? Error { get; init; }
    public string? ErrorCode { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public required TimeSpan Duration { get; init; }
}

/// <summary>The status of one diagnostic step after it ran (or was skipped/cancelled).</summary>
public sealed record DiagnosticStepResult
{
    public required string StepId { get; init; }
    public required CheckStatus Status { get; init; }
    public bool Required { get; init; } = true;
    public FailureClass Classification { get; init; } = FailureClass.Unknown;

    /// <summary>Set when the step executed and produced (or failed to produce) evidence.</summary>
    public DiagnosticEvidence? Evidence { get; init; }

    /// <summary>Set when the step was SKIPPED because a dependency was unavailable.</summary>
    public string? SkipReason { get; init; }
}

/// <summary>Outcome of a step evaluator: whether the observed data satisfies the check.</summary>
public sealed record StepEvaluation
{
    public required bool Passed { get; init; }
    public bool Warning { get; init; }
    public string? Detail { get; init; }

    public static StepEvaluation Pass() => new() { Passed = true };
    public static StepEvaluation PassWithWarning(string detail) => new() { Passed = true, Warning = true, Detail = detail };
    public static StepEvaluation Fail(string detail) => new() { Passed = false, Detail = detail };
}

/// <summary>Context handed to argument providers and evaluators mid-workflow.</summary>
public sealed record DiagnosticStepContext
{
    public required DiagnosticTarget Target { get; init; }
    public required IReadOnlyList<DiagnosticStepResult> Completed { get; init; }

    public DiagnosticStepResult? Find(string stepId) => Completed.FirstOrDefault(s => s.StepId == stepId);

    public object? DataOf(string stepId) => Find(stepId)?.Evidence?.Data;
}

/// <summary>Computes the JSON tool arguments for a step from the target and already-collected evidence.</summary>
public delegate string DiagnosticArgumentProvider(DiagnosticStepContext context);

/// <summary>Interprets a tool's output data into a pass/fail/warning decision.</summary>
public delegate StepEvaluation DiagnosticStepEvaluator(object? data, DiagnosticStepContext context);