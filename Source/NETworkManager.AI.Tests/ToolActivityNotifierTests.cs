using NETworkManager.AI.Conversation;
using NETworkManager.AI.Models;
using NETworkManager.AI.Orchestration;
using Xunit;

namespace NETworkManager.AI.Tests;

public class ToolActivityNotifierTests
{
    [Fact]
    public void Orchestration_events_map_to_activities()
    {
        var notifier = new ToolActivityNotifier();
        var activities = new List<ToolActivity>();
        notifier.Activity += (_, a) => activities.Add(a);

        notifier.Log(ToolOrchestrationEvent.ToolExecutionStarted, new Dictionary<string, object?>
        {
            ["toolName"] = "ping",
            ["category"] = "Connectivity",
            ["callId"] = "c1",
        });

        notifier.Log(ToolOrchestrationEvent.ToolExecutionCompleted, new Dictionary<string, object?>
        {
            ["toolName"] = "ping",
            ["category"] = "Connectivity",
            ["durationMs"] = 42.5,
        });

        notifier.Log(ToolOrchestrationEvent.ToolExecutionFailed, new Dictionary<string, object?>
        {
            ["toolName"] = "dns_lookup",
            ["errorCode"] = "TimedOut",
        });

        notifier.Log(ToolOrchestrationEvent.ToolCallCancelled, new Dictionary<string, object?>
        {
            ["toolName"] = "traceroute",
        });

        Assert.Equal(4, activities.Count);
        Assert.Equal(ToolActivityStatus.Started, activities[0].Status);
        Assert.Equal("ping", activities[0].ToolName);
        Assert.Equal("Connectivity", activities[0].Category);

        Assert.Equal(ToolActivityStatus.Completed, activities[1].Status);
        Assert.Equal(TimeSpan.FromMilliseconds(42.5), activities[1].Duration);

        Assert.Equal(ToolActivityStatus.Failed, activities[2].Status);
        Assert.Equal("TimedOut", activities[2].Summary);

        Assert.Equal(ToolActivityStatus.Cancelled, activities[3].Status);
    }

    [Fact]
    public void Diagnostic_events_map_to_activities_with_step_and_tool()
    {
        var notifier = new ToolActivityNotifier();
        var activities = new List<ToolActivity>();
        notifier.Activity += (_, a) => activities.Add(a);

        notifier.Log(Diagnostics.DiagnosticLogEvent.DiagnosticStepStarted, new Dictionary<string, object?>
        {
            ["stepId"] = "dns_lookup",
            ["toolName"] = "dns_lookup",
        });

        notifier.Log(Diagnostics.DiagnosticLogEvent.DiagnosticStepCompleted, new Dictionary<string, object?>
        {
            ["stepId"] = "dns_lookup",
            ["toolName"] = "dns_lookup",
            ["status"] = "Failed",
        });

        Assert.Equal(2, activities.Count);
        Assert.Equal(ToolActivityStatus.Started, activities[0].Status);
        Assert.Equal("dns_lookup", activities[0].ToolName);
        Assert.Equal("dns_lookup", activities[0].StepId);

        Assert.Equal(ToolActivityStatus.Completed, activities[1].Status);
        Assert.Equal("failed", activities[1].Summary);
    }

    [Fact]
    public void Activity_records_carry_no_sensitive_values()
    {
        var notifier = new ToolActivityNotifier();
        var activities = new List<ToolActivity>();
        notifier.Activity += (_, a) => activities.Add(a);

        // Even if a (hypothetical) caller put secret-looking data into the log payload, the notifier
        // only projects known safe fields — nothing else can leak into the UI.
        notifier.Log(ToolOrchestrationEvent.ToolExecutionStarted, new Dictionary<string, object?>
        {
            ["toolName"] = "ping",
            ["secret"] = "super-secret-api-key",
            ["authorization"] = "Bearer abc123",
        });

        var activity = Assert.Single(activities);
        Assert.Equal("ping", activity.ToolName);
        Assert.Null(activity.Summary);
        Assert.Null(activity.StepId);
        Assert.DoesNotContain("super-secret-api-key", $"{activity.ToolName}{activity.Category}{activity.StepId}{activity.Summary}");
        Assert.DoesNotContain("abc123", $"{activity.ToolName}{activity.Category}{activity.StepId}{activity.Summary}");
    }

    [Fact]
    public void OnToolActivity_forwards_to_observers()
    {
        var notifier = new ToolActivityNotifier();
        var seen = new List<ToolActivity>();
        notifier.Activity += (_, a) => seen.Add(a);

        notifier.OnToolActivity(new ToolActivity { ToolName = "ping", Status = ToolActivityStatus.Started });

        var activity = Assert.Single(seen);
        Assert.Equal(ToolActivityStatus.Started, activity.Status);
    }
}