namespace NETworkManager.AI.Diagnostics;

/// <summary>
///     Deterministic analyzer: maps the first failed required check to a <see cref="FailureClass"/> and produces
///     evidence-based findings, a summary, and a recommendation. It reports symptoms, never unproven root causes.
/// </summary>
public sealed class DefaultDiagnosticAnalyzer : IDiagnosticAnalyzer
{
    public DiagnosticAnalysis Analyze(DiagnosticReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var findings = report.Steps
            .Select(FormatFinding)
            .ToList();

        var classification = FailureClass.None;
        DiagnosticStepResult? failed = null;

        if (report.Status == DiagnosticStatus.Failed)
        {
            failed = report.Steps.FirstOrDefault(s => s.Required && s.Status == CheckStatus.Failed);
            if (failed is not null)
                classification = failed.Evidence?.ErrorCode == "TimedOut" ? FailureClass.Timeout : failed.Classification;
            else
                classification = FailureClass.Unknown;
        }

        return new DiagnosticAnalysis
        {
            Status = report.Status,
            Classification = classification,
            Findings = findings,
            Summary = Summarize(report, classification),
            Recommendation = Recommend(classification),
        };
    }

    private static string FormatFinding(DiagnosticStepResult step) => step.Status switch
    {
        CheckStatus.Passed => $"✓ {step.StepId}: passed",
        CheckStatus.Warning => $"✓ {step.StepId}: passed with warning",
        CheckStatus.Failed => $"✗ {step.StepId}: failed",
        CheckStatus.Skipped => $"− {step.StepId}: skipped{(step.SkipReason is null ? string.Empty : $" ({step.SkipReason})")}",
        CheckStatus.Cancelled => $"! {step.StepId}: cancelled",
        _ => step.StepId,
    };

    private static string Summarize(DiagnosticReport report, FailureClass classification)
    {
        if (classification == FailureClass.None)
        {
            return report.Status switch
            {
                DiagnosticStatus.Success => $"{report.Name}: no failures detected.",
                DiagnosticStatus.Warning => $"{report.Name}: completed with warnings.",
                DiagnosticStatus.Cancelled => $"{report.Name}: cancelled.",
                _ => $"{report.Name}: no failures detected.",
            };
        }

        return $"{report.Name}: {Classify(classification)}.";
    }

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

    private static string? Recommend(FailureClass classification) => classification switch
    {
        FailureClass.NoNetworkAdapter => "Check physical link and network adapter status.",
        FailureClass.NoIpAddress => "Check DHCP or static IPv4 configuration.",
        FailureClass.InvalidIpConfiguration => "Verify IP configuration (address, subnet, gateway).",
        FailureClass.NoDefaultGateway => "Check the default route and gateway configuration.",
        FailureClass.GatewayUnreachable => "Check connectivity to the default gateway.",
        FailureClass.DnsFailure => "Check the configured DNS servers.",
        FailureClass.ExternalConnectivityFailure => "Check upstream connectivity beyond the gateway.",
        FailureClass.TcpConnectivityFailure => "Check the target host, service, and any firewall.",
        FailureClass.RouteFailure => "Inspect the local routing table.",
        FailureClass.Timeout => "Increase the diagnostic timeout or check target responsiveness.",
        FailureClass.None => null,
        _ => "Review the failed evidence.",
    };
}