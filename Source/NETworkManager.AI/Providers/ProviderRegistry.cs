using NETworkManager.AI.Abstractions;

namespace NETworkManager.AI.Providers;

/// <summary>In-memory AI provider registry with explicit provider selection. Manual DI (consistent with the codebase).</summary>
public sealed class ProviderRegistry : IAIProviderRegistry
{
    private readonly Dictionary<string, IAIProvider> _providers = new(StringComparer.OrdinalIgnoreCase);
    private string? _selectedName;

    public IReadOnlyList<string> Names => _providers.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();

    public void Register(string name, IAIProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Provider name must not be empty.", nameof(name));

        _providers[name] = provider;
    }

    public bool TryGet(string name, out IAIProvider? provider) => _providers.TryGetValue(name, out provider);

    public void Select(string? name)
    {
        if (name is not null && !_providers.ContainsKey(name))
            throw new KeyNotFoundException($"No AI provider registered with the name '{name}'.");

        _selectedName = name;
    }

    public IAIProvider Resolve(string? name = null)
    {
        var key = name ?? _selectedName;

        if (key is null)
            throw new InvalidOperationException("No AI provider is selected.");

        if (!_providers.TryGetValue(key, out var provider))
            throw new KeyNotFoundException($"No AI provider registered with the name '{key}'.");

        return provider;
    }
}