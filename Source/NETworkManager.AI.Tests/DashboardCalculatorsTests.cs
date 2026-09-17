using NETworkManager.AI.Dashboard;
using NETworkManager.AI.Monitoring;
using NETworkManager.AI.Persistence;
using NETworkManager.AI.Tests.Fakes;
using Xunit;

namespace NETworkManager.AI.Tests;

public class DashboardCalculatorsTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [Fact]
    public void Availability_excludes_unknown_and_counts_non_healthy()
    {
        var entries = new[]
        {
            DashboardTestData.ResultEntry("gw", MonitoringResultStatus.Healthy, Now.AddMinutes(-1)),
            DashboardTestData.ResultEntry("gw", MonitoringResultStatus.Healthy, Now.AddMinutes(-2)),
            DashboardTestData.ResultEntry("gw", MonitoringResultStatus.Timeout, Now.AddMinutes(-3)),
            DashboardTestData.ResultEntry("gw", MonitoringResultStatus.Unknown, Now.AddMinutes(-4)),
        };

        var (percent, healthy, total) = DashboardCalculators.ComputeAvailability(entries, Now.AddHours(-1));

        Assert.Equal(2, healthy);
        Assert.Equal(3, total); // unknown excluded
        Assert.Equal(200.0 / 3.0, percent!.Value, 3);
    }

    [Fact]
    public void Availability_ignores_entries_before_window()
    {
        var entries = new[]
        {
            DashboardTestData.ResultEntry("gw", MonitoringResultStatus.Healthy, Now.AddHours(-3)),
        };

        var (percent, _, total) = DashboardCalculators.ComputeAvailability(entries, Now.AddHours(-1));

        Assert.Null(percent);
        Assert.Equal(0, total);
    }

    [Fact]
    public void Availability_empty_returns_null_percent()
    {
        var (percent, healthy, total) = DashboardCalculators.ComputeAvailability(Array.Empty<MonitoringHistoryEntry>(), Now.AddHours(-1));

        Assert.Null(percent);
        Assert.Equal(0, healthy);
        Assert.Equal(0, total);
    }

    [Fact]
    public void Staleness_null_last_check_is_stale()
    {
        Assert.True(DashboardCalculators.IsStale(null, Now, TimeSpan.FromMinutes(2)));
    }

    [Fact]
    public void Staleness_beyond_threshold_is_stale()
    {
        Assert.True(DashboardCalculators.IsStale(Now.AddMinutes(-3), Now, TimeSpan.FromMinutes(2)));
        Assert.False(DashboardCalculators.IsStale(Now.AddMinutes(-1), Now, TimeSpan.FromMinutes(2)));
    }

    [Fact]
    public void Trend_health_maps_correctly()
    {
        Assert.Equal(TrendHealth.Healthy, DashboardCalculators.ToTrendHealth(NetworkHealthStatus.Healthy));
        Assert.Equal(TrendHealth.Degraded, DashboardCalculators.ToTrendHealth(NetworkHealthStatus.Degraded));
        Assert.Equal(TrendHealth.Unhealthy, DashboardCalculators.ToTrendHealth(NetworkHealthStatus.Unhealthy));
        Assert.Equal(TrendHealth.Unknown, DashboardCalculators.ToTrendHealth(NetworkHealthStatus.Unknown));
    }
}
