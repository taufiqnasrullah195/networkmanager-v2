using System.Collections;
using System.Reflection;
using NETworkManager.AI.Abstractions;

namespace NETworkManager.AI.Models;

/// <summary>Builds provider-neutral <see cref="AIToolDefinition"/>s from registered tools (name, description, risk, and a simple reflected input schema).</summary>
public static class ToolDefinitionBuilder
{
    public static AIToolDefinition Build(INetworkTool tool)
    {
        var schema = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var property in tool.InputType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            schema[property.Name] = ToSchemaType(property.PropertyType);

        return new AIToolDefinition
        {
            Name = tool.Name,
            Description = tool.Description,
            RiskLevel = tool.RiskLevel.ToString(),
            InputSchema = schema,
        };
    }

    public static IReadOnlyList<AIToolDefinition> BuildAll(IToolRegistry registry)
        => registry.List().Select(Build).ToArray();

    private static string ToSchemaType(Type type)
    {
        if (type == typeof(string)) return "string";
        if (type == typeof(bool)) return "boolean";
        if (type == typeof(int) || type == typeof(long) || type == typeof(short)
            || type == typeof(byte) || type == typeof(ushort) || type == typeof(uint)
            || type == typeof(ulong) || type == typeof(sbyte)) return "integer";
        if (type == typeof(float) || type == typeof(double) || type == typeof(decimal)) return "number";
        if (type.IsEnum) return "string";
        if (type.IsArray || typeof(IEnumerable).IsAssignableFrom(type)) return "array";

        return "object";
    }
}