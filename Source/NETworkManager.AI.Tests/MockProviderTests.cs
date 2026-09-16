using NETworkManager.AI.Abstractions;
using NETworkManager.AI.Models;
using NETworkManager.AI.Providers;
using Xunit;

namespace NETworkManager.AI.Tests;

public class MockProviderTests
{
    [Fact]
    public async Task Default_returns_success_text()
    {
        var provider = new MockAIProvider();

        var response = await provider.SendAsync(new AIRequest { UserPrompt = "hi" });

        Assert.True(response.Success);
        Assert.Equal("Mock response.", response.Text);
        Assert.Equal(AIFinishReason.Stop, response.FinishReason);
    }

    [Fact]
    public async Task Handler_can_return_a_tool_call()
    {
        var provider = new MockAIProvider((_, _) => Task.FromResult(new AIResponse
        {
            Success = true,
            ToolCalls = new[] { new AIToolCall { CallId = "c1", ToolName = "ping", ArgumentsJson = "{\"target\":\"10.0.0.1\"}" } },
            FinishReason = AIFinishReason.ToolCalls,
        }));

        var response = await provider.SendAsync(new AIRequest { UserPrompt = "test" });

        var call = Assert.Single(response.ToolCalls!);
        Assert.Equal("ping", call.ToolName);
        Assert.Equal("c1", call.CallId);
    }

    [Fact]
    public async Task Handler_can_simulate_provider_unavailable()
    {
        var provider = new MockAIProvider((_, _) => throw new ProviderException(ProviderErrorCode.ProviderUnavailable, "down"));

        var ex = await Assert.ThrowsAsync<ProviderException>(() => provider.SendAsync(new AIRequest { UserPrompt = "x" }));

        Assert.Equal(ProviderErrorCode.ProviderUnavailable, ex.ErrorCode);
    }

    [Fact]
    public void Capabilities_reports_chat_and_tool_calling()
    {
        var provider = new MockAIProvider();

        Assert.Equal(AIProviderCapability.Chat | AIProviderCapability.ToolCalling, provider.Capabilities);
    }
}