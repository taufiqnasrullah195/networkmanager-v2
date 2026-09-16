using NETworkManager.AI.Conversation;
using NETworkManager.AI.Diagnostics;
using Xunit;

namespace NETworkManager.AI.Tests;

public class ConversationModelsTests
{
    [Fact]
    public void AIEvidenceContext_is_built_from_report_and_analysis()
    {
        var evidence = new DiagnosticEvidence
        {
            StepId = "dns_lookup",
            Tool = "dns_lookup",
            Success = false,
            Error = "DNS resolution failed.",
            ErrorCode = "CheckFailed",
            Timestamp = DateTimeOffset.UtcNow,
            Duration = TimeSpan.FromMilliseconds(5),
        };

        var report = new DiagnosticReport
        {
            DiagnosticId = "d1",
            Name = "Internet Connectivity Diagnostic",
            Target = DiagnosticTarget.Empty,
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Status = DiagnosticStatus.Failed,
            Steps = new[] { new DiagnosticStepResult { StepId = "dns_lookup", Status = CheckStatus.Failed, Evidence = evidence } },
        };

        var analysis = new DiagnosticAnalysis
        {
            Status = DiagnosticStatus.Failed,
            Classification = FailureClass.DnsFailure,
        };

        var context = AIEvidenceContext.From(report, analysis);

        Assert.Equal("d1", context.DiagnosticId);
        Assert.Equal(FailureClass.DnsFailure, context.Classification);
        Assert.Single(context.Evidence);
        Assert.Equal(new[] { "dns_lookup" }, context.FailedChecks);
    }

    [Fact]
    public void System_instructions_include_anti_fabrication_and_access_rules()
    {
        var prompt = NetworkDiagnosticInstructions.BaseSystemPrompt;

        Assert.Contains("Never fabricate results", prompt, StringComparison.Ordinal);
        Assert.Contains("Distinguish observed facts from inferences", prompt, StringComparison.Ordinal);
        Assert.Contains("only registered tools may be used", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("never attempt to bypass tool restrictions", prompt, StringComparison.OrdinalIgnoreCase);
    }
}