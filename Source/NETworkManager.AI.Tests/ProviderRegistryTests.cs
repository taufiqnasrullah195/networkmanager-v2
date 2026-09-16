using NETworkManager.AI.Abstractions;
using NETworkManager.AI.Models;
using NETworkManager.AI.Providers;
using Xunit;

namespace NETworkManager.AI.Tests;

public class ProviderRegistryTests
{
    private static MockAIProvider Dummy() => new();

    [Fact]
    public void Register_then_Names_lists_provider()
    {
        var registry = new ProviderRegistry();
        registry.Register("mock", Dummy());

        Assert.Equal(new[] { "mock" }, registry.Names);
    }

    [Fact]
    public void TryGet_is_case_insensitive()
    {
        var registry = new ProviderRegistry();
        registry.Register("Mock", Dummy());

        Assert.True(registry.TryGet("mock", out var provider));
        Assert.NotNull(provider);
    }

    [Fact]
    public void Select_then_Resolve_returns_selected_provider()
    {
        var registry = new ProviderRegistry();
        var provider = Dummy();
        registry.Register("mock", provider);

        registry.Select("mock");

        Assert.Same(provider, registry.Resolve());
    }

    [Fact]
    public void Resolve_by_name_returns_requested_provider()
    {
        var registry = new ProviderRegistry();
        var provider = Dummy();
        registry.Register("mock", provider);

        Assert.Same(provider, registry.Resolve("mock"));
    }

    [Fact]
    public void Resolve_without_selection_throws()
    {
        var registry = new ProviderRegistry();

        Assert.Throws<InvalidOperationException>(() => registry.Resolve());
    }

    [Fact]
    public void Select_unknown_name_throws()
    {
        var registry = new ProviderRegistry();

        Assert.Throws<KeyNotFoundException>(() => registry.Select("missing"));
    }

    [Fact]
    public void Resolve_unknown_name_throws()
    {
        var registry = new ProviderRegistry();

        Assert.Throws<KeyNotFoundException>(() => registry.Resolve("missing"));
    }

    [Fact]
    public void Register_duplicate_name_overwrites()
    {
        var registry = new ProviderRegistry();
        var first = Dummy();
        var second = Dummy();
        registry.Register("mock", first);
        registry.Register("mock", second);

        Assert.Same(second, registry.Resolve("mock"));
    }

    [Fact]
    public void Register_null_provider_throws()
    {
        var registry = new ProviderRegistry();

        Assert.Throws<ArgumentNullException>(() => registry.Register("mock", null!));
    }

    [Fact]
    public void Register_empty_name_throws()
    {
        var registry = new ProviderRegistry();

        Assert.Throws<ArgumentException>(() => registry.Register(" ", Dummy()));
    }
}