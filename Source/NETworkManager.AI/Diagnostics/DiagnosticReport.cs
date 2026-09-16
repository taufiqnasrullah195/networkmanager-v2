namespace NETworkManager.AI.Diagnostics;

/// <summary>Structured result of a diagnostic run. Evidence order is preserved (execution order).</summary>
public sealed record DiagnosticReport
{
    public required string DiagnosticId { get; init; }
    public required string Name { get; init; }
    public required DiagnosticTarget Target { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public required DateTimeOffset CompletedAt { get; init; }
    public required DiagnosticStatus Status { get; init; }
    public IReadOnlyList<DiagnosticStepResult> Steps { get; init; } = Array.Empty<DiagnosticStepResult>();
    public string? Summary { get; init; }

    // Derived views (computed from Steps so they can never disagree).
    public IReadOnlyList<DiagnosticEvidence> Evidence =>
        Steps.Where(s => s.Evidence is not null).Select(s => s.Evidence!).ToArray();

    public IReadOnlyList<DiagnosticStepResult> FailedChecks =>
        Steps.Where(s => s.Status == CheckStatus.Failed).ToArray();
}

/// <summary>Deterministic analysis of a diagnostic report: classification, findings, and an evidence-based recommendation.</summary>
public sealed record DiagnosticAnalysis
{
    public required DiagnosticStatus Status { get; init; }
    public FailureClass Classification { get; init; } = FailureClass.None;
    public IReadOnlyList<string> Findings { get; init; } = Array.Empty<string>();
    public string? Summary { get; init; }
    public string? Recommendation { get; init; }
}