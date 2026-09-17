using NETworkManager.AI.Dashboard;
using Xunit;

namespace NETworkManager.AI.Tests;

public class DashboardSecurityTests
{
    [Theory]
    [InlineData(typeof(DashboardSnapshot))]
    [InlineData(typeof(DeviceHealthRow))]
    [InlineData(typeof(AlertCard))]
    [InlineData(typeof(RecentEvent))]
    [InlineData(typeof(InterfaceIssue))]
    [InlineData(typeof(LatencyItem))]
    [InlineData(typeof(AvailabilityItem))]
    [InlineData(typeof(BandwidthItem))]
    public void Dashboard_models_have_no_secret_properties(Type type)
    {
        var properties = type.GetProperties().Select(p => p.Name).ToList();

        Assert.DoesNotContain(properties, p =>
            p.Contains("Community", StringComparison.OrdinalIgnoreCase)
            || p.Contains("Password", StringComparison.OrdinalIgnoreCase)
            || p.Contains("Secret", StringComparison.OrdinalIgnoreCase)
            || p.Contains("Token", StringComparison.OrdinalIgnoreCase)
            || p.Contains("Credential", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Aggregator_is_read_only_presentation_without_mutation_or_ai_surface()
    {
        var declared = typeof(DashboardAggregator)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Where(m => m.DeclaringType == typeof(DashboardAggregator))
            .Select(m => m.Name)
            .ToList();

        Assert.DoesNotContain(declared, n =>
            n.Contains("Add", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Remove", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Delete", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Execute", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Acknowledge", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Resolve", StringComparison.OrdinalIgnoreCase));
    }
}
