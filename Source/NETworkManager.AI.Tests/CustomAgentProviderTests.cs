using System.Net;
using System.Text.Json;
using NETworkManager.AI.Abstractions;
using NETworkManager.AI.Models;
using NETworkManager.AI.Providers;
using NETworkManager.AI.Providers.Authentication;
using NETworkManager.AI.Tests.Fakes;
using Xunit;

namespace NETworkManager.AI.Tests;

public class CustomAgentProviderTests
{
    private static CustomAgentConfiguration ValidConfiguration() =>
        new() { Name = "test-agent", Endpoint = "https://agent.example.internal/api/v1" };

    private static (CustomAgentProvider Provider, MockCustomAgentHandler Handler) CreateProvider(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler,
        CustomAgentConfiguration? configuration = null,
        IAgentAuthentication? authentication = null)
    {
        var mockHandler = new MockCustomAgentHandler(handler);
        var provider = new CustomAgentProvider(configuration ?? ValidConfiguration(), authentication, mockHandler);
        return (provider, mockHandler);
    }

    private static AIRequest Request() => new() { UserPrompt = "Why can't I reach 10.10.20.50?" };

    [Fact]
    public async Task SendAsync_parses_plain_response()
    {
        var (provider, _) = CreateProvider((_, _) => Task.FromResult(MockCustomAgentHandler.Json(
            """{"success":true,"message":"Network connectivity looks normal.","finishReason":"stop","usage":{"inputTokens":10,"outputTokens":5}}""")));

        var response = await provider.SendAsync(Request());

        Assert.True(response.Success);
        Assert.Equal("Network connectivity looks normal.", response.Text);
        Assert.Equal(AIFinishReason.Stop, response.FinishReason);
        Assert.Equal(10, response.Usage!.InputTokens);
        Assert.Equal(5, response.Usage.OutputTokens);
    }

    [Fact]
    public async Task SendAsync_parses_tool_calls()
    {
        var (provider, _) = CreateProvider((_, _) => Task.FromResult(MockCustomAgentHandler.Json(
            """{"success":true,"message":"I need to test connectivity first.","toolCalls":[{"id":"call-001","toolName":"ping","arguments":{"target":"10.10.20.50"}}]}""")));

        var response = await provider.SendAsync(Request());

        var call = Assert.Single(response.ToolCalls!);
        Assert.Equal("call-001", call.CallId);
        Assert.Equal("ping", call.ToolName);
        Assert.Contains("10.10.20.50", call.ArgumentsJson);
    }

    [Fact]
    public async Task SendAsync_posts_to_chat_endpoint_and_serializes_request()
    {
        var (provider, handler) = CreateProvider((_, _) => Task.FromResult(MockCustomAgentHandler.Json("""{"success":true}""")));

        await provider.SendAsync(new AIRequest
        {
            SystemPrompt = "You are a network engineer.",
            UserPrompt = "Why can't I reach 10.10.20.50?",
            ConversationId = "conv-1",
            Tools = new[] { new AIToolDefinition { Name = "ping", Description = "Test ICMP connectivity", RiskLevel = "Low", InputSchema = new Dictionary<string, string> { ["target"] = "string" } } },
            Context = new Dictionary<string, string> { ["hostname"] = "SRV-01" },
        });

        Assert.Equal("https://agent.example.internal/api/v1/chat", handler.LastRequest!.RequestUri!.ToString());
        Assert.Equal(HttpMethod.Post, handler.LastRequest.Method);

        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        var root = doc.RootElement;

        Assert.Equal("1", root.GetProperty("protocolVersion").GetString());
        Assert.Equal("conv-1", root.GetProperty("conversationId").GetString());
        Assert.Equal("You are a network engineer.", root.GetProperty("systemPrompt").GetString());
        Assert.Equal("Why can't I reach 10.10.20.50?", root.GetProperty("message").GetString());
        Assert.Equal("ping", root.GetProperty("tools")[0].GetProperty("name").GetString());
        Assert.Equal("Test ICMP connectivity", root.GetProperty("tools")[0].GetProperty("description").GetString());
        Assert.Equal("Low", root.GetProperty("tools")[0].GetProperty("riskLevel").GetString());
        Assert.Equal("string", root.GetProperty("tools")[0].GetProperty("inputSchema").GetProperty("target").GetString());
        Assert.Equal("SRV-01", root.GetProperty("context").GetProperty("hostname").GetString());
    }

    [Fact]
    public async Task SendAsync_times_out_when_agent_is_slow()
    {
        var configuration = ValidConfiguration() with { Timeout = TimeSpan.FromMilliseconds(200) };
        var (provider, _) = CreateProvider(
            async (_, ct) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                return MockCustomAgentHandler.Json("""{}""");
            },
            configuration);

        var ex = await Assert.ThrowsAsync<ProviderException>(() => provider.SendAsync(Request()));

        Assert.Equal(ProviderErrorCode.Timeout, ex.ErrorCode);
    }

    [Fact]
    public async Task SendAsync_honours_caller_cancellation()
    {
        var (provider, _) = CreateProvider(
            async (_, ct) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                return MockCustomAgentHandler.Json("""{}""");
            });

        using var cts = new CancellationTokenSource();
        var task = provider.SendAsync(Request(), cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    [Fact]
    public async Task SendAsync_maps_500_to_provider_unavailable()
    {
        var (provider, _) = CreateProvider((_, _) => Task.FromResult(MockCustomAgentHandler.Json("""boom""", HttpStatusCode.InternalServerError)));

        var ex = await Assert.ThrowsAsync<ProviderException>(() => provider.SendAsync(Request()));

        Assert.Equal(ProviderErrorCode.ProviderUnavailable, ex.ErrorCode);
    }

    [Fact]
    public async Task SendAsync_maps_401_to_authentication_failed()
    {
        var (provider, _) = CreateProvider((_, _) => Task.FromResult(MockCustomAgentHandler.Json("""unauthorized""", HttpStatusCode.Unauthorized)));

        var ex = await Assert.ThrowsAsync<ProviderException>(() => provider.SendAsync(Request()));

        Assert.Equal(ProviderErrorCode.AuthenticationFailed, ex.ErrorCode);
    }

    [Fact]
    public async Task SendAsync_maps_429_to_rate_limited()
    {
        var (provider, _) = CreateProvider((_, _) => Task.FromResult(MockCustomAgentHandler.Json("""slow down""", (HttpStatusCode)429)));

        var ex = await Assert.ThrowsAsync<ProviderException>(() => provider.SendAsync(Request()));

        Assert.Equal(ProviderErrorCode.RateLimited, ex.ErrorCode);
    }

    [Fact]
    public async Task SendAsync_maps_malformed_json_to_invalid_response()
    {
        var (provider, _) = CreateProvider((_, _) => Task.FromResult(MockCustomAgentHandler.Json("""not-json""")));

        var ex = await Assert.ThrowsAsync<ProviderException>(() => provider.SendAsync(Request()));

        Assert.Equal(ProviderErrorCode.InvalidResponse, ex.ErrorCode);
    }

    [Fact]
    public async Task SendAsync_maps_empty_body_to_invalid_response()
    {
        var (provider, _) = CreateProvider((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));

        var ex = await Assert.ThrowsAsync<ProviderException>(() => provider.SendAsync(Request()));

        Assert.Equal(ProviderErrorCode.InvalidResponse, ex.ErrorCode);
    }

    [Fact]
    public async Task SendAsync_applies_bearer_authentication()
    {
        var (provider, handler) = CreateProvider(
            (_, _) => Task.FromResult(MockCustomAgentHandler.Json("""{"success":true}""")),
            authentication: new BearerTokenAuthentication("secret-token"));

        await provider.SendAsync(Request());

        Assert.Equal("Bearer", handler.LastRequest!.Headers.Authorization!.Scheme);
        Assert.Equal("secret-token", handler.LastRequest.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task SendAsync_applies_api_key_authentication()
    {
        var (provider, handler) = CreateProvider(
            (_, _) => Task.FromResult(MockCustomAgentHandler.Json("""{"success":true}""")),
            authentication: new ApiKeyAuthentication("key-123"));

        await provider.SendAsync(Request());

        Assert.True(handler.LastRequest!.Headers.TryGetValues("X-Api-Key", out var values));
        Assert.Equal("key-123", Assert.Single(values));
    }

    [Fact]
    public async Task SendAsync_retries_transient_failure_then_fails()
    {
        var configuration = ValidConfiguration() with { MaxRetries = 1, RetryDelay = TimeSpan.FromMilliseconds(1) };
        var (provider, handler) = CreateProvider(
            (_, _) => Task.FromResult(MockCustomAgentHandler.Json("""boom""", HttpStatusCode.InternalServerError)),
            configuration);

        var ex = await Assert.ThrowsAsync<ProviderException>(() => provider.SendAsync(Request()));

        Assert.Equal(ProviderErrorCode.ProviderUnavailable, ex.ErrorCode);
        Assert.Equal(2, handler.CallCount); // initial + one retry
    }

    [Fact]
    public async Task SendAsync_does_not_retry_authentication_failure()
    {
        var configuration = ValidConfiguration() with { MaxRetries = 2, RetryDelay = TimeSpan.FromMilliseconds(1) };
        var (provider, handler) = CreateProvider(
            (_, _) => Task.FromResult(MockCustomAgentHandler.Json("""no""", HttpStatusCode.Unauthorized)),
            configuration);

        var ex = await Assert.ThrowsAsync<ProviderException>(() => provider.SendAsync(Request()));

        Assert.Equal(ProviderErrorCode.AuthenticationFailed, ex.ErrorCode);
        Assert.Equal(1, handler.CallCount); // auth failures are never retried
    }
}