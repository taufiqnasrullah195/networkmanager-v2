using NETworkManager.AI.Conversation;
using Xunit;

namespace NETworkManager.AI.Tests;

public class CopilotProviderConfigurationTests
{
    [Fact]
    public void Https_endpoint_is_valid()
    {
        var config = new CopilotProviderConfiguration
        {
            Enabled = true,
            Endpoint = "https://agent.internal.example",
            AuthenticationMode = "api-key",
            TimeoutSeconds = 30,
        };

        Assert.Empty(config.Validate());
        Assert.True(config.IsConfigured);
    }

    [Fact]
    public void Http_endpoint_is_rejected_without_explicit_dev_opt_in()
    {
        var config = new CopilotProviderConfiguration
        {
            Enabled = true,
            Endpoint = "http://localhost:8080",
            AuthenticationMode = "none",
        };

        Assert.Contains(config.Validate(), e => e.Contains("HTTPS", StringComparison.Ordinal));
        Assert.True(config.IsConfigured); // usable, but validation warns
    }

    [Fact]
    public void Http_endpoint_allowed_with_explicit_dev_opt_in()
    {
        var config = new CopilotProviderConfiguration
        {
            Enabled = true,
            Endpoint = "http://localhost:8080",
            AllowHttpForDevelopment = true,
        };

        Assert.Empty(config.Validate());
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-uri")]
    [InlineData("ftp://agent.example")]
    public void Invalid_endpoints_are_rejected(string endpoint)
    {
        var config = new CopilotProviderConfiguration { Enabled = true, Endpoint = endpoint };

        Assert.NotEmpty(config.Validate());
        Assert.False(config.IsConfigured);
    }

    [Fact]
    public void Disabled_configuration_is_always_valid()
    {
        var config = new CopilotProviderConfiguration { Enabled = false, Endpoint = "garbage" };

        Assert.Empty(config.Validate());
        Assert.False(config.IsConfigured);
    }

    [Fact]
    public void Timeout_and_auth_mode_are_validated()
    {
        var config = new CopilotProviderConfiguration
        {
            Enabled = true,
            Endpoint = "https://agent.example",
            AuthenticationMode = "basic",
            TimeoutSeconds = 0,
        };

        var errors = config.Validate();
        Assert.Contains(errors, e => e.Contains("AuthenticationMode", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("TimeoutSeconds", StringComparison.Ordinal));
    }

    [Fact]
    public void Maps_to_custom_agent_configuration()
    {
        var config = new CopilotProviderConfiguration
        {
            Enabled = true,
            Endpoint = "https://agent.example",
            AuthenticationMode = "bearer",
            TimeoutSeconds = 45,
        };

        var agentConfig = config.ToCustomAgentConfiguration();

        Assert.Equal("https://agent.example", agentConfig.Endpoint);
        Assert.Equal("bearer", agentConfig.AuthenticationMode);
        Assert.Equal(TimeSpan.FromSeconds(45), agentConfig.Timeout);
    }

    [Fact]
    public void Json_roundtrip_preserves_configuration()
    {
        var config = new CopilotProviderConfiguration
        {
            Enabled = true,
            Endpoint = "https://agent.example",
            AuthenticationMode = "api-key",
            AllowHttpForDevelopment = false,
            TimeoutSeconds = 90,
            CredentialKey = "custom-agent",
        };

        var restored = CopilotProviderConfiguration.FromJson(config.ToJson());

        Assert.NotNull(restored);
        Assert.Equal(config, restored);
    }

    [Fact]
    public void Json_contains_no_secret_material()
    {
        var config = new CopilotProviderConfiguration
        {
            Enabled = true,
            Endpoint = "https://agent.example",
            AuthenticationMode = "api-key",
        };

        var json = config.ToJson();

        // Only a credential *key reference* may appear — never a secret value.
        Assert.Contains("custom-agent", json, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-", json, StringComparison.Ordinal);
        Assert.DoesNotContain("apiKeyValue", json, StringComparison.Ordinal);
    }
}