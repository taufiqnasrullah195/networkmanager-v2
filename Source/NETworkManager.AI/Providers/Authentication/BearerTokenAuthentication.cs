using System.Net.Http;
using System.Net.Http.Headers;
using NETworkManager.AI.Abstractions;

namespace NETworkManager.AI.Providers.Authentication;

/// <summary>Adds an <c>Authorization: Bearer</c> header. The token is supplied by a delegate, never hard-coded.</summary>
public sealed class BearerTokenAuthentication : IAgentAuthentication
{
    private readonly Func<string> _tokenProvider;

    public BearerTokenAuthentication(string token) : this(() => token)
    {
    }

    public BearerTokenAuthentication(Func<string> tokenProvider)
    {
        _tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
    }

    public void Apply(HttpRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _tokenProvider());
    }
}