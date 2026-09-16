using System.Text;
using NETworkManager.AI.Abstractions;
using NETworkManager.AI.Diagnostics;
using NETworkManager.AI.Models;

namespace NETworkManager.AI.Conversation;

/// <summary>
///     Orchestrates evidence-based AI conversations: builds a provider-neutral request (system instructions +
///     history), runs the bounded tool loop, and turns the collected diagnostic evidence plus the provider's answer
///     into a structured <see cref="AIAnalysisResponse"/> that keeps facts, inferences, and recommendations separate.
/// </summary>
public sealed class AIConversationService : IAIConversationService
{
    private readonly IAgentToolLoop _toolLoop;
    private readonly IDiagnosticAnalyzer _analyzer;
    private readonly ISensitiveDataFilter _filter;
    private readonly int _maxContextMessages;
    private readonly Dictionary<string, AIConversation> _conversations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AIEvidenceContext> _latestEvidence = new(StringComparer.Ordinal);

    public AIConversationService(
        IAgentToolLoop toolLoop,
        IDiagnosticAnalyzer? analyzer = null,
        ISensitiveDataFilter? filter = null,
        int maxContextMessages = 20)
    {
        _toolLoop = toolLoop ?? throw new ArgumentNullException(nameof(toolLoop));
        _analyzer = analyzer ?? new DefaultDiagnosticAnalyzer();
        _filter = filter ?? new DefaultSensitiveDataFilter();
        _maxContextMessages = maxContextMessages > 0
            ? maxContextMessages
            : throw new ArgumentOutOfRangeException(nameof(maxContextMessages));
    }

    public async Task<AIConversationResult> SendAsync(string userMessage, string? conversationId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
            throw new ArgumentException("User message must not be empty.", nameof(userMessage));

        var conversation = GetOrCreate(conversationId);
        conversation.Status = ConversationStatus.Processing;
        conversation.UpdatedAt = DateTimeOffset.UtcNow;

        var history = ConversationHistory(conversation); // messages prior to this turn

        conversation.Messages.Add(new AIMessage { Role = AIMessageRole.User, Content = userMessage });
        TrimHistory(conversation);

        if (cancellationToken.IsCancellationRequested)
            return Complete(conversation, ConversationStatus.Cancelled, ErrorResponse("The request was cancelled."));

        var request = new AIRequest
        {
            SystemPrompt = BuildSystemPrompt(conversation.ConversationId),
            UserPrompt = _filter.Redact(userMessage),
            Conversation = history,
        };

        AgentLoopResult loop;
        try
        {
            loop = await _toolLoop.RunAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Complete(conversation, ConversationStatus.Cancelled, ErrorResponse("The request was cancelled."));
        }
        catch (ProviderException ex) when (ex.ErrorCode == ProviderErrorCode.Timeout)
        {
            return Complete(conversation, ConversationStatus.Failed, ErrorResponse("The AI provider timed out. Please try again."));
        }
        catch (ProviderException ex) when (ex.ErrorCode == ProviderErrorCode.AuthenticationFailed)
        {
            return Complete(conversation, ConversationStatus.Failed, ErrorResponse("Authentication with the AI provider failed. Check the configured credential."));
        }
        catch (ProviderException)
        {
            return Complete(conversation, ConversationStatus.Failed, ErrorResponse("The AI provider is unavailable. Please try again."));
        }
        catch (TimeoutException)
        {
            return Complete(conversation, ConversationStatus.Failed, ErrorResponse("The AI provider timed out. Please try again."));
        }
        catch (Exception)
        {
            return Complete(conversation, ConversationStatus.Failed, ErrorResponse("An unexpected error occurred while processing your request."));
        }

        if (!loop.FinalResponse.Success)
            return Complete(conversation, ConversationStatus.Failed, ErrorResponse("The AI provider could not process the request."));

        if (string.IsNullOrWhiteSpace(loop.FinalResponse.Text)
            && loop.ToolResults.Count == 0
            && (loop.FinalResponse.ToolCalls is null || loop.FinalResponse.ToolCalls.Count == 0))
            return Complete(conversation, ConversationStatus.Failed, ErrorResponse("The AI provider returned an empty response."));

        // Record what was measured so later turns keep the provenance.
        foreach (var toolResult in loop.ToolResults)
        {
            conversation.Messages.Add(new AIMessage
            {
                Role = AIMessageRole.Tool,
                ToolResults = new[] { toolResult },
                Content = $"Tool '{toolResult.ToolName}' returned {(toolResult.Success ? "success" : $"error {toolResult.ErrorCode ?? toolResult.Error}")}.",
            });
        }

        var response = BuildAnalysis(loop, conversation.ConversationId);

        conversation.Messages.Add(new AIMessage
        {
            Role = AIMessageRole.Assistant,
            Content = response.Summary ?? response.RawText,
        });
        TrimHistory(conversation);

        return Complete(
            conversation,
            ConversationStatus.Completed,
            response,
            loop.Rounds,
            loop.Executions,
            loop.LimitReached);
    }

    private AIAnalysisResponse BuildAnalysis(AgentLoopResult loop, string conversationId)
    {
        var findings = new List<AIFinding>();
        var evidenceReferences = new List<string>();
        var contexts = new List<AIEvidenceContext>();
        var recommendations = new List<string>();
        var possibleCauses = new List<string>();
        DiagnosticReport? report = null;
        var classification = FailureClass.None;
        var hasEvidence = false;

        foreach (var toolResult in loop.ToolResults)
        {
            if (toolResult.Data is DiagnosticReport diagnosticReport)
            {
                report = diagnosticReport;
                hasEvidence = true;

                var analysis = _analyzer.Analyze(diagnosticReport);
                classification = analysis.Classification;

                var context = AIEvidenceContext.From(diagnosticReport, analysis);
                contexts.Add(context);
                _latestEvidence[conversationId] = context;

                AddObservationFindings(diagnosticReport, findings, evidenceReferences);

                if (classification != FailureClass.None)
                {
                    findings.Add(new AIFinding
                    {
                        Title = analysis.Summary ?? Classify(classification),
                        Type = AIFindingType.Inference,
                        Confidence = AIConfidence.Supported,
                        EvidenceReferences = FailedStepReferences(diagnosticReport),
                        Severity = AIFindingSeverity.High,
                    });

                    if (analysis.Recommendation is not null)
                    {
                        recommendations.Add(analysis.Recommendation);
                        findings.Add(new AIFinding
                        {
                            Title = analysis.Recommendation,
                            Type = AIFindingType.Recommendation,
                            Confidence = AIConfidence.Possible,
                        });
                    }

                    foreach (var cause in PossibleCausesFor(classification))
                        possibleCauses.Add(cause);
                }
                else
                {
                    findings.Add(new AIFinding
                    {
                        Title = analysis.Summary ?? "No failures detected.",
                        Type = AIFindingType.Observation,
                        Confidence = AIConfidence.Confirmed,
                    });
                }

                continue;
            }

            hasEvidence = true;
            evidenceReferences.Add(toolResult.ToolName);

            if (toolResult.Success && toolResult.Data is not null)
            {
                findings.Add(new AIFinding { Title = $"{toolResult.ToolName}: passed", Type = AIFindingType.Observation, EvidenceReferences = new[] { toolResult.ToolName } });
            }
            else
            {
                findings.Add(new AIFinding
                {
                    Title = $"{toolResult.ToolName}: {(toolResult.Error ?? "failed")}",
                    Type = AIFindingType.Warning,
                    EvidenceReferences = new[] { toolResult.ToolName },
                    Severity = AIFindingSeverity.High,
                });
            }
        }

        var confidence = !hasEvidence
            ? AIConfidence.Unknown
            : classification != FailureClass.None ? AIConfidence.Supported : AIConfidence.Confirmed;

        var summary = string.IsNullOrWhiteSpace(loop.FinalResponse.Text)
            ? report?.Summary
            : loop.FinalResponse.Text;

        return new AIAnalysisResponse
        {
            Summary = summary,
            Findings = findings,
            EvidenceReferences = evidenceReferences,
            EvidenceContexts = contexts,
            PossibleCauses = possibleCauses,
            Recommendations = recommendations,
            ToolCalls = loop.FinalResponse.ToolCalls,
            Confidence = confidence,
            RawText = loop.FinalResponse.Text,
        };
    }

    private static void AddObservationFindings(
        DiagnosticReport report,
        ICollection<AIFinding> findings,
        ICollection<string> evidenceReferences)
    {
        foreach (var step in report.Steps)
        {
            if (step.Evidence is null)
                continue; // skipped/cancelled: nothing was observed

            var reference = $"{report.DiagnosticId}:{step.StepId}";
            evidenceReferences.Add(reference);

            findings.Add(new AIFinding
            {
                Title = step.Status == CheckStatus.Failed
                    ? step.Evidence.Error ?? $"{step.StepId} failed"
                    : $"{step.StepId} passed",
                Type = AIFindingType.Observation,
                EvidenceReferences = new[] { reference },
                Severity = step.Status == CheckStatus.Failed ? AIFindingSeverity.High : AIFindingSeverity.Info,
                Confidence = AIConfidence.Confirmed,
            });
        }
    }

    private static string[] FailedStepReferences(DiagnosticReport report) =>
        report.Steps
            .Where(s => s.Status == CheckStatus.Failed && s.Evidence is not null)
            .Select(s => $"{report.DiagnosticId}:{s.StepId}")
            .ToArray();

    private string BuildSystemPrompt(string conversationId)
    {
        var sb = new StringBuilder(NetworkDiagnosticInstructions.BaseSystemPrompt);

        if (_latestEvidence.TryGetValue(conversationId, out var evidence))
        {
            sb.AppendLine();
            sb.Append(FormatEvidence(evidence));
        }

        return sb.ToString();
    }

    private static string FormatEvidence(AIEvidenceContext evidence)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Prior diagnostic evidence (observed facts only)");
        sb.AppendLine($"Diagnostic: {evidence.DiagnosticName} ({evidence.DiagnosticId})");
        sb.AppendLine($"Timestamp: {evidence.Timestamp:O}");

        foreach (var item in evidence.Evidence)
            sb.AppendLine($"- {item.StepId}: {(item.Success ? "passed" : "failed")}");

        if (evidence.Classification != FailureClass.None)
            sb.AppendLine($"Classification: {evidence.Classification}");

        return sb.ToString();
    }

    private static IReadOnlyList<ChatMessage>? ConversationHistory(AIConversation conversation)
    {
        if (conversation.Messages.Count == 0)
            return null;

        return conversation.Messages
            .Select(m => new ChatMessage(m.Role.ToString().ToLowerInvariant(), m.Content ?? string.Empty))
            .ToList();
    }

    private void TrimHistory(AIConversation conversation)
    {
        while (conversation.Messages.Count > _maxContextMessages)
            conversation.Messages.RemoveAt(0);
    }

    private AIConversation GetOrCreate(string? conversationId)
    {
        if (!string.IsNullOrWhiteSpace(conversationId) && _conversations.TryGetValue(conversationId, out var existing))
            return existing;

        var conversation = new AIConversation
        {
            ConversationId = string.IsNullOrWhiteSpace(conversationId) ? Guid.NewGuid().ToString("N") : conversationId,
        };

        _conversations[conversation.ConversationId] = conversation;
        return conversation;
    }

    private static AIConversationResult Complete(
        AIConversation conversation,
        ConversationStatus status,
        AIAnalysisResponse response,
        int rounds = 0,
        int executions = 0,
        bool limitReached = false)
    {
        conversation.Status = status;
        conversation.UpdatedAt = DateTimeOffset.UtcNow;

        return new AIConversationResult
        {
            ConversationId = conversation.ConversationId,
            Status = status,
            Response = response,
            Rounds = rounds,
            ToolExecutions = executions,
            ToolCallLimitReached = limitReached,
        };
    }

    private static AIAnalysisResponse ErrorResponse(string summary) => new()
    {
        Summary = summary,
        Confidence = AIConfidence.Unknown,
    };

    private static string Classify(FailureClass classification) => classification switch
    {
        FailureClass.NoNetworkAdapter => "no active network adapter detected",
        FailureClass.NoIpAddress => "no IPv4 address detected on an active adapter",
        FailureClass.InvalidIpConfiguration => "invalid IP configuration detected",
        FailureClass.NoDefaultGateway => "no default route detected",
        FailureClass.GatewayUnreachable => "the default gateway is unreachable",
        FailureClass.DnsFailure => "DNS resolution failed",
        FailureClass.ExternalConnectivityFailure => "external connectivity failed",
        FailureClass.TcpConnectivityFailure => "TCP connectivity failed",
        FailureClass.RouteFailure => "a routing problem was detected",
        FailureClass.Timeout => "a diagnostic step timed out",
        _ => "a diagnostic check failed",
    };

    private static string[] PossibleCausesFor(FailureClass classification) => classification switch
    {
        FailureClass.DnsFailure => new[]
        {
            "DNS server unavailable (unverified)",
            "Incorrect DNS configuration (unverified)",
            "DNS filtering or blocking (unverified)",
        },
        FailureClass.NoNetworkAdapter => new[]
        {
            "Network cable disconnected (unverified)",
            "Network adapter disabled (unverified)",
        },
        FailureClass.NoIpAddress => new[]
        {
            "DHCP server unavailable (unverified)",
            "Static IP misconfigured (unverified)",
        },
        FailureClass.NoDefaultGateway => new[] { "No default route configured (unverified)" },
        FailureClass.GatewayUnreachable => new[]
        {
            "Gateway offline or filtered (unverified)",
            "VLAN/subnet mismatch (unverified)",
        },
        FailureClass.ExternalConnectivityFailure => new[]
        {
            "Upstream/ISP issue (unverified)",
            "Egress firewall blocking (unverified)",
        },
        FailureClass.TcpConnectivityFailure => new[]
        {
            "Service not running (unverified)",
            "Firewall blocking the port (unverified)",
        },
        _ => Array.Empty<string>(),
    };
}