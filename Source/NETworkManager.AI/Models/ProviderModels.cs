namespace NETworkManager.AI.Models;

/// <summary>Capabilities an AI provider may support. Providers may implement only a subset.</summary>
[Flags]
public enum AIProviderCapability
{
    None = 0,
    Chat = 1 << 0,
    ToolCalling = 1 << 1,
    Streaming = 1 << 2,
    Vision = 1 << 3,
    StructuredOutput = 1 << 4,
    Embeddings = 1 << 5,
}

/// <summary>Provider-neutral error categories.</summary>
public enum ProviderErrorCode
{
    ProviderUnavailable,
    AuthenticationFailed,
    Timeout,
    InvalidRequest,
    InvalidResponse,
    ToolCallInvalid,
    RateLimited,
    NetworkError,
    Unknown,
}

/// <summary>A provider failure, surfaced with a stable machine-readable <see cref="ProviderErrorCode"/>.</summary>
public sealed class ProviderException : Exception
{
    public ProviderErrorCode ErrorCode { get; }

    public ProviderException(ProviderErrorCode errorCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }
}

/// <summary>Configuration for a custom-agent endpoint. Credentials are never stored here — see <c>IAgentAuthentication</c>.</summary>
public sealed record CustomAgentConfiguration
{
    public required string Name { get; init; }

    /// <summary>API base URL (scheme + host + optional path). The provider POSTs to <c>{Endpoint}/chat</c>.</summary>
    public required string Endpoint { get; init; }

    public string ProtocolVersion { get; init; } = "1";

    public bool Enabled { get; init; } = true;

    /// <summary>Informational: "none" | "api-key" | "bearer". The actual secret lives in <c>IAgentAuthentication</c>.</summary>
    public string? AuthenticationMode { get; init; }

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>Bounded retry count for transient failures (network/timeout/rate-limit). 0 = no retry.</summary>
    public int MaxRetries { get; init; }

    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(1);

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(Name))
            errors.Add("Name must not be empty.");

        if (string.IsNullOrWhiteSpace(Endpoint)
            || !Uri.TryCreate(Endpoint, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            errors.Add("Endpoint must be an absolute http/https URI.");

        if (Timeout <= TimeSpan.Zero)
            errors.Add("Timeout must be greater than zero.");

        if (MaxRetries is < 0 or > 5)
            errors.Add("MaxRetries must be between 0 and 5.");

        return errors;
    }
}