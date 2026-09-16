using NETworkManager.AI.Models;
using Xunit;

namespace NETworkManager.AI.Tests;

public class CustomAgentConfigurationTests
{
    private static CustomAgentConfiguration Valid() =>
        new() { Name = "agent", Endpoint = "https://agent.example.internal/api/v1" };

    [Fact]
    public void Valid_config_has_no_errors()
    {
        Assert.Empty(Valid().Validate());
    }

    [Fact]
    public void Missing_name_is_invalid()
    {
        var config = Valid() with { Name = " " };

        Assert.Contains(config.Validate(), e => e.Contains("Name", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Relative_endpoint_is_invalid()
    {
        var config = Valid() with { Endpoint = "agent.internal/api/v1" };

        Assert.Contains(config.Validate(), e => e.Contains("Endpoint", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Non_http_scheme_is_invalid()
    {
        var config = Valid() with { Endpoint = "ftp://agent.internal/api/v1" };

        Assert.NotEmpty(config.Validate());
    }

    [Fact]
    public void Http_endpoint_is_allowed_for_local_development()
    {
        var config = Valid() with { Endpoint = "http://localhost:8080/api/v1" };

        Assert.Empty(config.Validate());
    }

    [Fact]
    public void Zero_timeout_is_invalid()
    {
        var config = Valid() with { Timeout = TimeSpan.Zero };

        Assert.Contains(config.Validate(), e => e.Contains("Timeout", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Negative_retries_is_invalid()
    {
        var config = Valid() with { MaxRetries = -1 };

        Assert.Contains(config.Validate(), e => e.Contains("MaxRetries", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Too_many_retries_is_invalid()
    {
        var config = Valid() with { MaxRetries = 6 };

        Assert.Contains(config.Validate(), e => e.Contains("MaxRetries", StringComparison.OrdinalIgnoreCase));
    }
}