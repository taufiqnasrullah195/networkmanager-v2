namespace NETworkManager.AI.Abstractions;

/// <summary>Registers and selects AI providers. Deals only in discovery/selection — it never sends requests.</summary>
public interface IAIProviderRegistry
{
    /// <summary>Names of all registered providers, in case-insensitive sort order.</summary>
    IReadOnlyList<string> Names { get; }

    void Register(string name, IAIProvider provider);

    bool TryGet(string name, out IAIProvider? provider);

    /// <summary>Selects the provider used when <see cref="Resolve()"/> is called without a name. <c>null</c> clears selection.</summary>
    void Select(string? name);

    /// <summary>Resolves the named provider, or the currently selected provider when <paramref name="name"/> is <c>null</c>.</summary>
    IAIProvider Resolve(string? name = null);
}