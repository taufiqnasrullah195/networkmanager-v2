using System.Net.Http;

namespace NETworkManager.AI.Abstractions;

/// <summary>
///     Applies authentication to an outgoing HTTP request for a custom agent endpoint.
///     The secret is supplied by the implementation at runtime (from secure configuration/credential store) and is
///     never hard-coded or logged.
/// </summary>
public interface IAgentAuthentication
{
    void Apply(HttpRequestMessage request);
}