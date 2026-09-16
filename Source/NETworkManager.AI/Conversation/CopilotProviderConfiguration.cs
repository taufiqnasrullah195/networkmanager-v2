using System.Text.Json;
using NETworkManager.AI.Diagnostics;
using NETworkManager.AI.Models;

namespace NETworkManager.AI.Conversation;

/// <summary>
///     Runtime configuration for the AI copilot's custom-agent provider. Persisted as plain JSON (no secrets —
///     credentials live in <c>ISecureCredentialStore</c> and are referenced only by <see cref="CredentialKey"/>).
/// </summary>
public sealed record CopilotProviderConfiguration
{
    /// <summary>Custom agent is enabled; when false the copilot falls back to the development mock provider.</summary>
    public bool Enabled { get; init; }

    /// <summary>Custom agent endpoint (https required for production; http only with an explicit dev flag).</summary>
    public string? Endpoint { get; init; }

    /// <summary>"none" | "api-key" | "bearer".</summary>
    public string AuthenticationMode { get; init; } = "none";

    /// <summary>Explicit, development-only opt-in for plain http endpoints. Never silently applied.</summary>
    public bool AllowHttpForDevelopment { get; init; }

    public int TimeoutSeconds { get; init; } = 60;

    /// <summary>Key under which the credential is stored in the secure credential store.</summary>
    public string CredentialKey { get; init; } = "custom-agent";

    /// <summary>True when the configuration points at a usable custom agent.</summary>
    public bool IsConfigured =>
        Enabled
        && !string.IsNullOrWhiteSpace(Endpoint)
        && Uri.TryCreate(Endpoint, UriKind.Absolute, out var uri)
        && uri.Scheme is "https" or "http";

    /// <summary>Structural validation; empty when valid.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (!Enabled)
            return errors;

        if (string.IsNullOrWhiteSpace(Endpoint) || !Uri.TryCreate(Endpoint, UriKind.Absolute, out var uri))
        {
            errors.Add("Endpoint must be an absolute URI.");
            return errors;
        }

        if (uri.Scheme == Uri.UriSchemeHttps)
        {
            // production transport — ok
        }
        else if (uri.Scheme == Uri.UriSchemeHttp)
        {
            if (!AllowHttpForDevelopment)
                errors.Add("Endpoint must use HTTPS (plain HTTP requires the explicit development-only opt-in).");
        }
        else
        {
            errors.Add("Endpoint scheme must be http or https.");
        }

        if (AuthenticationMode is not ("none" or "api-key" or "bearer"))
            errors.Add("AuthenticationMode must be none, api-key, or bearer.");

        if (AuthenticationMode != "none" && string.IsNullOrWhiteSpace(CredentialKey))
            errors.Add("CredentialKey must not be empty when authentication is configured.");

        if (TimeoutSeconds is < 1 or > 300)
            errors.Add("TimeoutSeconds must be between 1 and 300.");

        return errors;
    }

    /// <summary>Maps to the Step 4 provider configuration.</summary>
    public CustomAgentConfiguration ToCustomAgentConfiguration() => new()
    {
        Name = "Custom Agent",
        Endpoint = Endpoint!,
        AuthenticationMode = AuthenticationMode,
        Timeout = TimeSpan.FromSeconds(Math.Clamp(TimeoutSeconds, 1, 300)),
    };

    /// <summary>Serializes for persistence (contains no secrets).</summary>
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static CopilotProviderConfiguration? FromJson(string? json) =>
        string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<CopilotProviderConfiguration>(json, JsonOptions);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}