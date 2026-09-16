using NETworkManager.AI.Diagnostics;
using NETworkManager.AI.Diagnostics.Workflows;
using Xunit;

namespace NETworkManager.AI.Tests;

public class DiagnosticModelsTests
{
    [Fact]
    public void Internet_connectivity_workflow_has_ordered_steps()
    {
        var workflow = InternetConnectivityDiagnostic.Create();

        Assert.Equal("Internet Connectivity Diagnostic", workflow.Name);
        Assert.Equal(8, workflow.Steps.Count);
        Assert.Equal(
            new[] { "adapter", "ip_config", "default_route", "gateway_ping", "dns_lookup", "external_ping", "tcp_test", "traceroute" },
            workflow.Steps.Select(s => s.Id));
    }

    [Fact]
    public void Internet_connectivity_workflow_validates_cleanly()
    {
        Assert.Empty(InternetConnectivityDiagnostic.Create().Validate());
    }

    [Fact]
    public void Workflow_rejects_duplicate_ids_and_missing_dependency()
    {
        var workflow = new DiagnosticWorkflow
        {
            Name = "broken",
            Severity = "LOW",
            Steps = new[]
            {
                new DiagnosticStep { Id = "a", Name = "a", ToolName = "t" },
                new DiagnosticStep { Id = "a", Name = "dup", ToolName = "t" },
                new DiagnosticStep { Id = "c", Name = "c", ToolName = "t", DependsOn = new[] { "missing" } },
            },
        };

        var errors = workflow.Validate();

        Assert.Contains(errors, e => e.Contains("Duplicate step Id 'a'", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("depends on 'missing'", StringComparison.Ordinal));
    }

    [Fact]
    public void Workflow_rejects_self_dependency()
    {
        var workflow = new DiagnosticWorkflow
        {
            Name = "self-dep",
            Severity = "LOW",
            Steps = new[] { new DiagnosticStep { Id = "a", Name = "a", ToolName = "t", DependsOn = new[] { "a" } } },
        };

        Assert.Contains(workflow.Validate(), e => e.Contains("cannot depend on itself", StringComparison.Ordinal));
    }
}