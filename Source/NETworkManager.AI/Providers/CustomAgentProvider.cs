using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using NETworkManager.AI.Abstractions;
using NETworkManager.AI.Models;

namespace NETworkManager.AI.Providers;

/// <summary>
///     Provider that talks to an external/custom AI agent over a documented HTTP protocol
///     (see <c>docs/CUSTOM_AGENT_PROTOCOL.md</c>). The agent is a reasoning/orchestration endpoint only: it can
///     <em>request</em> tools, but it can never execute them. Tool execution stays inside TheWiseNetwork's registry
///     and execution service. Transport/authentication/parse/timeout failures are surfaced as <see cref="ProviderException"/>.
/// </summary>
public sealed class CustomAgentProvider : IAIProvider, IDisposable
{
    private const string ChatPath = "chat";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly CustomAgentConfiguration _configuration;
    private readonly IAgentAuthentication? _authentication;

    public AIProviderCapability Capabilities => AIProviderCapability.Chat | AIProviderCapability.ToolCalling;

    public CustomAgentProvider(
        CustomAgentConfiguration configuration,
        IAgentAuthentication? authentication = null,
        HttpMessageHandler? handler = null)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

        var validationErrors = configuration.Validate();
        if (validationErrors.Count > 0)
            throw new ArgumentException($"Invalid custom agent configuration: {string.Join(" ", validationErrors)}");

        _authentication = authentication;

        // Default handler keeps standard TLS certificate validation (no "accept all certificates" path).
        var actualHandler = handler ?? new HttpClientHandler();
        var ownsHandler = handler is null;

        _httpClient = new HttpClient(actualHandler, disposeHandler: ownsHandler)
        {
            BaseAddress = new Uri(configuration.Endpoint.TrimEnd('/') + "/"),
            Timeout = Timeout.InfiniteTimeSpan, // per-request timeout is enforced via linked cancellation
        };
    }

    public async Task<AIResponse> SendAsync(AIRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var attempt = 0;

        while (true)
        {
            try
            {
                return await SendOnceAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (ProviderException ex) when (IsTransient(ex.ErrorCode) && attempt < _configuration.MaxRetries)
            {
                attempt++;
                await Task.Delay(_configuration.RetryDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static bool IsTransient(ProviderErrorCode code) =>
        code is ProviderErrorCode.ProviderUnavailable or ProviderErrorCode.NetworkError
            or ProviderErrorCode.Timeout or ProviderErrorCode.RateLimited;

    private async Task<AIResponse> SendOnceAsync(AIRequest request, CancellationToken cancellationToken)
    {
        using var timeoutCts = new CancellationTokenSource(_configuration.Timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        using var httpRequest = BuildHttpRequest(request);
        _authentication?.Apply(httpRequest);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(httpRequest, linkedCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ProviderException(ProviderErrorCode.Timeout,
                $"Custom agent '{_configuration.Name}' did not respond within {_configuration.Timeout.TotalSeconds:0.#} seconds.");
        }
        catch (OperationCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw; // propagate caller cancellation
        }
        catch (HttpRequestException ex)
        {
            throw new ProviderException(ProviderErrorCode.NetworkError,
                $"Custom agent '{_configuration.Name}' could not be reached: {ex.Message}", ex);
        }

        using (response)
        {
            return await InterpretResponseAsync(response).ConfigureAwait(false);
        }
    }

    private HttpRequestMessage BuildHttpRequest(AIRequest request)
    {
        var dto = new ChatRequestDto(
            _configuration.ProtocolVersion,
            request.ConversationId,
            request.SystemPrompt,
            request.UserPrompt,
            request.Conversation?.Select(m => new MessageDto(m.Role, m.Content)).ToArray(),
            request.Tools?.Select(t => new ToolDefinitionDto(t.Name, t.Description, t.RiskLevel, t.InputSchema)).ToArray(),
            request.ToolResults?.Select(r => new ToolResultDto(r.ToolName, r.Success, r.Data, r.Error, r.ErrorCode)).ToArray(),
            request.Context,
            request.Metadata);

        var json = JsonSerializer.Serialize(dto, JsonOptions);

        return new HttpRequestMessage(HttpMethod.Post, ChatPath)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }

    private static async Task<AIResponse> InterpretResponseAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new ProviderException(ProviderErrorCode.AuthenticationFailed,
                $"Custom agent rejected authentication (HTTP {(int)response.StatusCode}).");

        if (response.StatusCode == (HttpStatusCode)429)
            throw new ProviderException(ProviderErrorCode.RateLimited, "Custom agent rate-limited the request.");

        if (response.StatusCode != HttpStatusCode.OK)
            throw new ProviderException(ProviderErrorCode.ProviderUnavailable,
                $"Custom agent returned HTTP {(int)response.StatusCode}.");

        if (string.IsNullOrWhiteSpace(body))
            throw new ProviderException(ProviderErrorCode.InvalidResponse, "Custom agent returned an empty response.");

        ChatResponseDto dto;
        try
        {
            dto = JsonSerializer.Deserialize<ChatResponseDto>(body, JsonOptions)
                  ?? throw new ProviderException(ProviderErrorCode.InvalidResponse, "Custom agent returned a null response.");
        }
        catch (JsonException ex)
        {
            throw new ProviderException(ProviderErrorCode.InvalidResponse, $"Custom agent returned an unparseable response: {ex.Message}", ex);
        }

        return new AIResponse
        {
            Success = dto.Success,
            Text = dto.Message,
            ToolCalls = dto.ToolCalls?.Select(tc => new AIToolCall
            {
                CallId = string.IsNullOrWhiteSpace(tc.Id) ? Guid.NewGuid().ToString("N") : tc.Id,
                ToolName = tc.ToolName,
                ArgumentsJson = tc.Arguments is { } args ? args.GetRawText() : "{}",
            }).ToArray(),
            FinishReason = ParseFinishReason(dto.FinishReason),
            Error = dto.Error,
            ErrorCode = ParseErrorCode(dto.ErrorCode),
            Usage = dto.Usage is null ? null : new AIUsage
            {
                InputTokens = dto.Usage.InputTokens,
                OutputTokens = dto.Usage.OutputTokens,
                TotalTokens = dto.Usage.TotalTokens,
            },
            Metadata = dto.Metadata,
        };
    }

    private static AIFinishReason ParseFinishReason(string? value) => value?.ToLowerInvariant() switch
    {
        "stop" => AIFinishReason.Stop,
        "length" => AIFinishReason.Length,
        "tool_calls" => AIFinishReason.ToolCalls,
        "content_filtered" => AIFinishReason.ContentFiltered,
        _ => AIFinishReason.Unknown,
    };

    private static ProviderErrorCode? ParseErrorCode(string? value) => value?.ToLowerInvariant() switch
    {
        "provider_unavailable" => ProviderErrorCode.ProviderUnavailable,
        "authentication_failed" => ProviderErrorCode.AuthenticationFailed,
        "timeout" => ProviderErrorCode.Timeout,
        "invalid_request" => ProviderErrorCode.InvalidRequest,
        "invalid_response" => ProviderErrorCode.InvalidResponse,
        "tool_call_invalid" => ProviderErrorCode.ToolCallInvalid,
        "rate_limited" => ProviderErrorCode.RateLimited,
        "network_error" => ProviderErrorCode.NetworkError,
        "unknown" => ProviderErrorCode.Unknown,
        _ => null,
    };

    public void Dispose() => _httpClient.Dispose();

    // --- Protocol transfer objects (camelCase on the wire via JsonSerializerDefaults.Web) ---

    private sealed record MessageDto(string Role, string Content);

    private sealed record ToolDefinitionDto(string Name, string Description, string RiskLevel, IReadOnlyDictionary<string, string> InputSchema);

    private sealed record ToolResultDto(string ToolName, bool Success, object? Data, string? Error, string? ErrorCode);

    private sealed record ChatRequestDto(
        string ProtocolVersion,
        string? ConversationId,
        string? SystemPrompt,
        string? Message,
        IReadOnlyList<MessageDto>? Conversation,
        IReadOnlyList<ToolDefinitionDto>? Tools,
        IReadOnlyList<ToolResultDto>? ToolResults,
        IReadOnlyDictionary<string, string>? Context,
        IReadOnlyDictionary<string, string>? Metadata);

    private sealed record ToolCallDto(string? Id, string ToolName, JsonElement? Arguments);

    private sealed record UsageDto(int? InputTokens, int? OutputTokens, int? TotalTokens);

    private sealed record ChatResponseDto(
        bool Success,
        string? Message,
        string? FinishReason,
        IReadOnlyList<ToolCallDto>? ToolCalls,
        UsageDto? Usage,
        string? Error,
        string? ErrorCode,
        IReadOnlyDictionary<string, string>? Metadata);
}