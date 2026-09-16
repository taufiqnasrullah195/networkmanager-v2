namespace NETworkManager.AI.Abstractions;

/// <summary>
///     Central registry of available network tools.
///     The registry answers "which tools exist?"; it does NOT execute anything.
/// </summary>
public interface IToolRegistry
{
    /// <summary>Registers a tool under its <see cref="INetworkTool.Name"/>. Replacing an existing name is idempotent.</summary>
    void Register(INetworkTool tool);

    /// <summary>Attempts to find a tool by name (case-insensitive).</summary>
    bool TryGet(string name, out INetworkTool? tool);

    /// <summary>Lists all registered tools ordered by name.</summary>
    IReadOnlyList<INetworkTool> List();

    /// <summary>Returns <c>true</c> if a tool with the given name is registered.</summary>
    bool Contains(string name);
}