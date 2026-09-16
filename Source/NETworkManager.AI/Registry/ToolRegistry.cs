using NETworkManager.AI.Abstractions;

namespace NETworkManager.AI.Registry;

/// <summary>Thread-safe in-memory tool registry. Answers discovery; does not execute.</summary>
public sealed class ToolRegistry : IToolRegistry
{
    private readonly Dictionary<string, INetworkTool> _tools = new(StringComparer.OrdinalIgnoreCase);

    public void Register(INetworkTool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);

        if (string.IsNullOrWhiteSpace(tool.Name))
            throw new ArgumentException("Tool name must not be empty.", nameof(tool));

        _tools[tool.Name] = tool;
    }

    public bool TryGet(string name, out INetworkTool? tool) => _tools.TryGetValue(name, out tool);

    public IReadOnlyList<INetworkTool> List() =>
        _tools.Values.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();

    public bool Contains(string name) => _tools.ContainsKey(name);
}