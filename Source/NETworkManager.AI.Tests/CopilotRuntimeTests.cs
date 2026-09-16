using NETworkManager.AI.Conversation;
using NETworkManager.AI.Tests.Fakes;
using Xunit;

namespace NETworkManager.AI.Tests;

public class SecureCredentialStoreTests
{
    [Fact]
    public async Task Store_get_remove_roundtrip()
    {
        var store = new InMemorySecureCredentialStore();

        await store.StoreAsync("custom-agent", "secret-value");
        Assert.True(await store.HasCredentialAsync("custom-agent"));
        Assert.Equal("secret-value", await store.GetAsync("custom-agent"));

        await store.RemoveAsync("custom-agent");
        Assert.False(await store.HasCredentialAsync("custom-agent"));
        Assert.Null(await store.GetAsync("custom-agent"));
    }

    [Fact]
    public async Task Get_missing_key_returns_null()
    {
        var store = new InMemorySecureCredentialStore();

        Assert.Null(await store.GetAsync("does-not-exist"));
        Assert.False(await store.HasCredentialAsync("does-not-exist"));
    }

    [Fact]
    public async Task Remove_missing_key_is_noop()
    {
        var store = new InMemorySecureCredentialStore();

        await store.RemoveAsync("never-stored"); // must not throw
    }
}

public class CopilotEvidenceProjectionTests
{
    [Fact]
    public void Text_only_response_projects_no_evidence()
    {
        var response = new AIAnalysisResponse
        {
            Summary = "Ping to 8.8.8.8 failed and the gateway is down, believe me.",
            RawText = "Ping failed.",
            Confidence = AIConfidence.Unknown,
        };

        Assert.Empty(CopilotEvidenceProjection.Project(response));
    }

    [Fact]
    public void Observation_findings_project_as_structured_evidence()
    {
        var response = new AIAnalysisResponse
        {
            Findings = new[]
            {
                new AIFinding { Title = "ping: passed", Type = AIFindingType.Observation, EvidenceReferences = new[] { "ping" } },
                new AIFinding { Title = "dns_lookup: failed", Type = AIFindingType.Observation, Severity = AIFindingSeverity.High, EvidenceReferences = new[] { "dns_lookup" } },
            },
        };

        var items = CopilotEvidenceProjection.Project(response);

        Assert.Equal(2, items.Count);
        Assert.Equal("✓", items[0].Glyph);
        Assert.Equal("✗", items[1].Glyph);
        Assert.Equal("ping", items[0].Detail);
    }

    [Fact]
    public void Non_observation_findings_never_become_evidence()
    {
        var response = new AIAnalysisResponse
        {
            Findings = new[]
            {
                new AIFinding { Title = "DNS server is broken (guess)", Type = AIFindingType.Inference },
                new AIFinding { Title = "Check DNS servers", Type = AIFindingType.Recommendation },
            },
        };

        Assert.Empty(CopilotEvidenceProjection.Project(response)); // inference/recommendation are NOT evidence
    }
}