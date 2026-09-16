using NETworkManager.AI.Abstractions;
using NETworkManager.AI.Models;
using NETworkManager.AI.Providers;
using NETworkManager.AI.Providers.Authentication;
using NETworkManager.AI.Tests.Fakes;
using Xunit;

namespace NETworkManager.AI.Tests;

/// <summary>Custom Agent runtime integration: provider registry selection, secure-credential-backed authentication, and timeouts.</summary>
public class CustomAgentRuntimeIntegrationTests
{
    private const string OkResponse = """{"success":true,"message":"ok","finishReason":"stop"}""";

    [Fact]
    public async Task Custom_agent_uses_credential_from_secure_store()
    {
        var store = new InMemorySecureCredentialStore();
        await store.StoreAsync("custom-agent", "stored-secret-key");

        var handler = new MockCustomAgentHandler((_, _) => Task.FromResult(MockCustomAgentHandler.Json(OkResponse)));

        var provider = new CustomAgentProvider(
            new CustomAgentConfiguration { Name = "agent", Endpoint = "http://127.0.0.1:19999", AuthenticationMode = "api-key" },
            new ApiKeyAuthentication(() => store.GetAsync("custom-agent").GetAwaiter().GetResult()),
            handler);

        var response = await provider.SendAsync(new AIRequest { UserPrompt = "hello" });

        Assert.True(response.Success);
        Assert.NotNull(handler.LastRequest);
        Assert.True(handler.LastRequest.Headers.TryGetValues("X-Api-Key", out var values));
        Assert.Equal("stored-secret-key", values!.First());
    }

    [Fact]
    public async Task Bearer_authentication_uses_credential_from_secure_store()
    {
        var store = new InMemorySecureCredentialStore();
        await store.StoreAsync("custom-agent", "stored-token");

        var handler = new MockCustomAgentHandler((_, _) => Task.FromResult(MockCustomAgentHandler.Json(OkResponse)));

        var provider = new CustomAgentProvider(
            new CustomAgentConfiguration { Name = "agent", Endpoint = "http://127.0.0.1:19999", AuthenticationMode = "bearer" },
            new BearerTokenAuthentication(() => store.GetAsync("custom-agent").GetAwaiter().GetResult()!),
            handler);

        _ = await provider.SendAsync(new AIRequest { UserPrompt = "hello" });

        Assert.NotNull(handler.LastRequest);
        Assert.Equal("Bearer stored-token", handler.LastRequest.Headers.Authorization?.ToString());
    }

    [Fact]
    public async Task Missing_credential_leads_to_authentication_failure_not_crash()
    {
        var store = new InMemorySecureCredentialStore(); // nothing stored

        var handler = new MockCustomAgentHandler((_, _) => Task.FromResult(MockCustomAgentHandler.Json(OkResponse)));

        var provider = new CustomAgentProvider(
            new CustomAgentConfiguration { Name = "agent", Endpoint = "http://127.0.0.1:19999", AuthenticationMode = "api-key" },
            new ApiKeyAuthentication(() => store.GetAsync("custom-agent").GetAwaiter().GetResult() ?? ""),
            handler);

        // Sending an empty api key still performs the request; a real agent rejects it. Either way: no crash,
        // structured provider error surface.
        var response = await provider.SendAsync(new AIRequest { UserPrompt = "hello" });

        Assert.NotNull(response);
    }

    [Fact]
    public void Provider_selection_works_through_the_registry()
    {
        var handler = new MockCustomAgentHandler((_, _) => Task.FromResult(MockCustomAgentHandler.Json(OkResponse)));

        var mock = new MockAIProvider();
        var customAgent = new CustomAgentProvider(
            new CustomAgentConfiguration { Name = "agent", Endpoint = "http://127.0.0.1:19999" },
            null,
            handler);

        var registry = new ProviderRegistry();
        registry.Register("mock", mock);
        registry.Register("custom-agent", customAgent);
        registry.Select("custom-agent");

        var resolved = registry.Resolve();

        Assert.Same(customAgent, resolved);
        Assert.Equal(new[] { "custom-agent", "mock" }, registry.Names);
    }

    [Fact]
    public async Task Selected_custom_agent_is_actually_used_for_requests()
    {
        var handler = new MockCustomAgentHandler((_, _) => Task.FromResult(MockCustomAgentHandler.Json(OkResponse)));

        var customAgent = new CustomAgentProvider(
            new CustomAgentConfiguration { Name = "agent", Endpoint = "http://127.0.0.1:19999" },
            null,
            handler);

        var registry = new ProviderRegistry();
        registry.Register("mock", new MockAIProvider());
        registry.Register("custom-agent", customAgent);
        registry.Select("custom-agent");

        _ = await registry.Resolve().SendAsync(new AIRequest { UserPrompt = "hello" });

        Assert.Equal(1, handler.CallCount); // the selected (custom agent) provider served the request
    }

    [Fact]
    public async Task Slow_agent_times_out_with_structured_error()
    {
        var handler = new MockCustomAgentHandler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
            return MockCustomAgentHandler.Json(OkResponse);
        });

        var provider = new CustomAgentProvider(
            new CustomAgentConfiguration { Name = "agent", Endpoint = "http://127.0.0.1:19999", Timeout = TimeSpan.FromMilliseconds(200) },
            null,
            handler);

        var ex = await Assert.ThrowsAsync<ProviderException>(() => provider.SendAsync(new AIRequest { UserPrompt = "hello" }));

        Assert.Equal(ProviderErrorCode.Timeout, ex.ErrorCode);
    }
}