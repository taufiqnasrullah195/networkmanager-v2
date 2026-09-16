namespace NETworkManager.AI.Models;

/// <summary>
///     Risk classification for a tool, driving future approval policy.
///     Invariant: never silently downgrade a risk level.
/// </summary>
public enum ToolRiskLevel
{
    Low = 0,
    Medium = 1,
    High = 2,
    Critical = 3,
}

/// <summary>Broad grouping used for tool discovery and documentation.</summary>
public enum ToolCategory
{
    Connectivity = 0,
    Resolution = 1,
    PathDiscovery = 2,
    NetworkInterface = 3,
    Routing = 4,
}