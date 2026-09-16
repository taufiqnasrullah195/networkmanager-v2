using System.Net.Http;
using NETworkManager.AI.Abstractions;

namespace NETworkManager.AI.Providers.Authentication;

/// <summary>Adds a configurable API-key header (default <c>X-Api-Key</c>). The key is supplied by a delegate, never hard-coded.</summary>
public sealed class ApiKeyAuthentication : IAgentAuthentication
{
    private readonly string _headerName;
    private readonly Func<string> _valueProvider;

    public ApiKeyAuthentication(string apiKey, string headerName = "X-Api-Key")
        : this(() => apiKey, headerName)
    {
    }

    public ApiKeyAuthentication(Func<string> valueProvider, string headerName = "X-Api-Key")
    {
        _valueProvider = valueProvider ?? throw new ArgumentNullException(nameof(valueProvider));
        _headerName = string.IsNullOrWhiteSpace(headerName) ? "X-Api-Key" : headerName;
    }

    public void Apply(HttpRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Headers.Add(_headerName, _valueProvider());
    }
}