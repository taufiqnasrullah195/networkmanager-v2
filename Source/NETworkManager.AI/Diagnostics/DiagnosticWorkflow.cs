using System.Text.Json;

namespace NETworkManager.AI.Diagnostics;

/// <summary>A single reusable step in a diagnostic workflow.</summary>
public sealed record DiagnosticStep
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }

    /// <summary>Name of the registered tool that collects evidence for this step (never a shell/command).</summary>
    public required string ToolName { get; init; }

    /// <summary>Static JSON arguments; overridden by <see cref="Arguments"/> when set.</summary>
    public string? ArgumentsJson { get; init; }

    /// <summary>Dynamic argument provider (may read the target and prior evidence).</summary>
    public DiagnosticArgumentProvider? Arguments { get; init; }

    /// <summary>Whether the whole diagnostic fails when this step fails. Non-required steps produce a warning at most.</summary>
    public bool Required { get; init; } = true;

    /// <summary>Per-step maximum duration.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>Step IDs that must have PASSED for this step to run; otherwise it is SKIPPED.</summary>
    public IReadOnlyList<string> DependsOn { get; init; } = Array.Empty<string>();

    /// <summary>Failure classification to assign when this step fails.</summary>
    public FailureClass ClassifiesAs { get; init; } = FailureClass.Unknown;

    /// <summary>Interprets the tool output into pass/fail; null means "pass when the tool reports success".</summary>
    public DiagnosticStepEvaluator? Evaluator { get; init; }
}

/// <summary>A named, ordered sequence of diagnostic steps.</summary>
public sealed record DiagnosticWorkflow
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required string Severity { get; init; }
    public required IReadOnlyList<DiagnosticStep> Steps { get; init; }
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Structural validation: unique, ordered step ids; valid tool names; valid dependencies; valid JSON arguments.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(Name))
            errors.Add("Name must not be empty.");

        if (Steps is null || Steps.Count == 0)
        {
            errors.Add("At least one step is required.");
            return errors;
        }

        if (Timeout <= TimeSpan.Zero)
            errors.Add("Timeout must be greater than zero.");

        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < Steps.Count; i++)
        {
            var step = Steps[i];

            if (string.IsNullOrWhiteSpace(step.Id))
            {
                errors.Add($"Step at index {i} has an empty Id.");
                continue;
            }

            if (!seen.Add(step.Id))
                errors.Add($"Duplicate step Id '{step.Id}'.");

            if (string.IsNullOrWhiteSpace(step.ToolName))
                errors.Add($"Step '{step.Id}' has an empty ToolName.");

            if (step.Timeout <= TimeSpan.Zero)
                errors.Add($"Step '{step.Id}' must have a positive Timeout.");

            foreach (var dep in step.DependsOn)
            {
                if (dep == step.Id)
                    errors.Add($"Step '{step.Id}' cannot depend on itself.");
                else if (!seen.Contains(dep))
                    errors.Add($"Step '{step.Id}' depends on '{dep}', which is not defined earlier.");
            }

            if (step.Arguments is null && step.ArgumentsJson is not null)
            {
                try
                {
                    _ = JsonDocument.Parse(step.ArgumentsJson);
                }
                catch (JsonException)
                {
                    errors.Add($"Step '{step.Id}' ArgumentsJson is not valid JSON.");
                }
            }
        }

        return errors;
    }
}