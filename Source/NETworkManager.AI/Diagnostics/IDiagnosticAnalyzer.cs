namespace NETworkManager.AI.Diagnostics;

/// <summary>Deterministic, evidence-based analysis of a diagnostic report. No AI, no probabilistic reasoning.</summary>
public interface IDiagnosticAnalyzer
{
    DiagnosticAnalysis Analyze(DiagnosticReport report);
}