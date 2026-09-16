using NETworkManager.AI.Diagnostics;
using NETworkManager.AI.Models;

namespace NETworkManager.AI.Conversation;

/// <summary>Role of a conversation message. Provider-neutral; mapped to vendor roles by the provider.</summary>
public enum AIMessageRole
{
    System,
    User,
    Assistant,
    Tool,
}

/// <summary>Lifecycle state of a conversation request.</summary>
public enum ConversationStatus
{
    Idle,
    Processing,
    WaitingForTool,
    WaitingForProvider,
    Completed,
    Failed,
    Cancelled,
}

/// <summary>Qualitative verification level of an AI claim (§8: fact vs inference). No fabricated numeric confidence.</summary>
public enum AIConfidence
{
    Unknown,
    Possible,
    Supported,
    Confirmed,
}

/// <summary>Deterministic kind of a finding — describes technical diagnostic state, not subjective ranking.</summary>
public enum AIFindingType
{
    Observation,
    Inference,
    Warning,
    Recommendation,
}

/// <summary>Severity of a finding, describing diagnostic impact only.</summary>
public enum AIFindingSeverity
{
    Info,
    Low,
    Medium,
    High,
    Critical,
}

/// <summary>A provider-neutral message in a conversation. May carry content, tool calls, and/or tool results.</summary>
public sealed record AIMessage
{
    public required AIMessageRole Role { get; init; }
    public string? Content { get; init; }
    public IReadOnlyList<AIToolCall>? ToolCalls { get; init; }
    public IReadOnlyList<AIToolResult>? ToolResults { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

/// <summary>In-memory conversation state. Not persisted (STEP 7).</summary>
public sealed class AIConversation
{
    public required string ConversationId { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ConversationStatus Status { get; set; } = ConversationStatus.Idle;
    public List<AIMessage> Messages { get; } = new();
    public IReadOnlyDictionary<string, string>? Metadata { get; set; }
}

/// <summary>One structured finding (observation, inference, warning, or recommendation) with evidence references.</summary>
public sealed record AIFinding
{
    public required string Title { get; init; }
    public string? Description { get; init; }
    public required AIFindingType Type { get; init; }
    public IReadOnlyList<string> EvidenceReferences { get; init; } = Array.Empty<string>();
    public AIFindingSeverity Severity { get; init; } = AIFindingSeverity.Info;
    public AIConfidence Confidence { get; init; } = AIConfidence.Unknown;
}

/// <summary>Structured, evidence-backed diagnostic context supplied to the AI (with provenance).</summary>
public sealed record AIEvidenceContext
{
    public required string DiagnosticId { get; init; }
    public required string DiagnosticName { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public DiagnosticTarget Target { get; init; } = DiagnosticTarget.Empty;
    public IReadOnlyList<DiagnosticEvidence> Evidence { get; init; } = Array.Empty<DiagnosticEvidence>();
    public IReadOnlyList<string> FailedChecks { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public FailureClass Classification { get; init; } = FailureClass.None;

    public static AIEvidenceContext From(DiagnosticReport report, DiagnosticAnalysis analysis) => new()
    {
        DiagnosticId = report.DiagnosticId,
        DiagnosticName = report.Name,
        Timestamp = report.CompletedAt,
        Target = report.Target,
        Evidence = report.Evidence,
        FailedChecks = report.FailedChecks.Select(f => f.StepId).ToArray(),
        Warnings = report.Steps.Where(s => s.Status == CheckStatus.Warning).Select(s => s.StepId).ToArray(),
        Classification = analysis.Classification,
    };
}

/// <summary>Provider-neutral structured response: facts, inferences, recommendations, and raw text.</summary>
public sealed record AIAnalysisResponse
{
    public string? Summary { get; init; }
    public IReadOnlyList<AIFinding> Findings { get; init; } = Array.Empty<AIFinding>();
    public IReadOnlyList<string> EvidenceReferences { get; init; } = Array.Empty<string>();
    public IReadOnlyList<AIEvidenceContext> EvidenceContexts { get; init; } = Array.Empty<AIEvidenceContext>();
    public IReadOnlyList<string> PossibleCauses { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Recommendations { get; init; } = Array.Empty<string>();
    public IReadOnlyList<AIToolCall>? ToolCalls { get; init; }
    public AIConfidence Confidence { get; init; } = AIConfidence.Unknown;
    public string? RawText { get; init; }
}

/// <summary>The outcome of a single conversation request.</summary>
public sealed record AIConversationResult
{
    public required string ConversationId { get; init; }
    public required ConversationStatus Status { get; init; }
    public required AIAnalysisResponse Response { get; init; }
    public int Rounds { get; init; }
    public int ToolExecutions { get; init; }
    public bool ToolCallLimitReached { get; init; }
}