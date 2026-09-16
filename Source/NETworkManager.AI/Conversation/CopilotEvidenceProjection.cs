namespace NETworkManager.AI.Conversation;

/// <summary>UI-neutral evidence display item: glyph (✓/✗/⚠), short title, and safe detail line.</summary>
public sealed record EvidenceDisplayItem(string Glyph, string Title, string Detail);

/// <summary>
///     Projects structured evidence (<see cref="AIAnalysisResponse.EvidenceContexts"/> and Observation findings) into
///     display items. Pure projection: it never parses AI text and never invents evidence — a text-only response
///     produces no evidence items, no matter what the text claims.
/// </summary>
public static class CopilotEvidenceProjection
{
    public static IReadOnlyList<EvidenceDisplayItem> Project(AIAnalysisResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        var items = new List<EvidenceDisplayItem>();

        foreach (var context in response.EvidenceContexts)
        {
            var warnings = new HashSet<string>(context.Warnings, StringComparer.Ordinal);

            foreach (var evidence in context.Evidence)
            {
                var glyph = warnings.Contains(evidence.StepId)
                    ? "⚠"
                    : evidence.Success ? "✓" : "✗";

                var detail = $"Tool: {evidence.Tool}";

                if (!string.IsNullOrWhiteSpace(evidence.Target))
                    detail += $" | Target: {evidence.Target}";

                detail += $" | {evidence.Timestamp:HH:mm:ss}";

                items.Add(new EvidenceDisplayItem(glyph, evidence.StepId, detail));
            }
        }

        // Single-tool responses (no diagnostic report): project Observation findings — still structured data.
        if (items.Count == 0)
        {
            foreach (var finding in response.Findings.Where(f => f.Type == AIFindingType.Observation))
            {
                var glyph = finding.Severity switch
                {
                    AIFindingSeverity.High => "✗",
                    AIFindingSeverity.Medium => "⚠",
                    _ => "✓",
                };

                items.Add(new EvidenceDisplayItem(glyph, finding.Title, string.Join(", ", finding.EvidenceReferences)));
            }
        }

        return items;
    }
}