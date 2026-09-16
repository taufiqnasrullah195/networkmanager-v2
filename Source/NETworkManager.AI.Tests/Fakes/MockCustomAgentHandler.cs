using System.Net;
using System.Net.Http;
using System.Text;

namespace NETworkManager.AI.Tests.Fakes;

/// <summary>
///     In-process fake for the custom-agent HTTP protocol. Captures the last request (for serialization assertions) and
///     returns whatever the supplied handler produces, so tests can simulate plain responses, tool calls, malformed
///     bodies, errors, and delays without a real network.
/// </summary>
public sealed class MockCustomAgentHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

    public HttpRequestMessage? LastRequest { get; private set; }

    public string? LastRequestBody { get; private set; }

    public int CallCount { get; private set; }

    public MockCustomAgentHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        => _handler = handler;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        CallCount++;
        return await _handler(request, cancellationToken).ConfigureAwait(false);
    }

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}