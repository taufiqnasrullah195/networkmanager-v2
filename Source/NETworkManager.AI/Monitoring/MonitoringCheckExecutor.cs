using NETworkManager.AI.Abstractions;
using NETworkManager.AI.Models;

namespace NETworkManager.AI.Monitoring;

/// <summary>
///     Maps a monitoring check to the existing read-only tool pipeline and maps the structured tool result into a
///     <see cref="MonitoringResult"/>. Reuses <c>ping</c>/<c>tcp_test</c>/<c>dns_lookup</c> — no duplicate
///     networking, no AI involvement, no shell.
/// </summary>
public sealed class MonitoringCheckExecutor : IMonitoringCheckExecutor
{
    private const int ExecutorOuterBoundBufferMilliseconds = 500;

    private readonly IToolExecutionService _execution;

    public MonitoringCheckExecutor(IToolExecutionService execution)
    {
        _execution = execution ?? throw new ArgumentNullException(nameof(execution));
    }

    public async Task<MonitoringResult> ExecuteAsync(MonitoringCheck check, MonitoringTarget target,
        CancellationToken cancellationToken = default)
    {
        // Gate cancellation before any tool dispatch — a fast tool must still observe an already-cancelled token.
        if (cancellationToken.IsCancellationRequested)
        {
            return Fail(check, target, MonitorErrorClass.Cancelled, MonitoringResultStatus.Cancelled,
                "Check was cancelled.", observed: null, started: DateTimeOffset.UtcNow, duration: TimeSpan.Zero);
        }

        var address = target.Address;

        if (string.IsNullOrWhiteSpace(address))
        {
            return Fail(check, target, MonitorErrorClass.ExecutionError, MonitoringResultStatus.Error,
                "Target has no IP address or hostname configured.", observed: null,
                started: DateTimeOffset.UtcNow, duration: TimeSpan.Zero);
        }

        var started = DateTimeOffset.UtcNow;
        var timeout = check.Timeout ?? TimeSpan.FromSeconds(5);

        var (toolName, input) = check.Type switch
        {
            MonitorCheckType.Ping => ("ping", (object)new PingInput
            {
                Target = address,
                Count = 4,
                TimeoutMilliseconds = ToMilliseconds(timeout),
            }),
            MonitorCheckType.TcpConnectivity => ("tcp_test", (object)new TcpTestInput
            {
                Host = address,
                Port = check.Port,
                TimeoutMilliseconds = ToMilliseconds(timeout),
            }),
            MonitorCheckType.DnsResolution => ("dns_lookup", (object)new DnsLookupInput
            {
                Host = address,
                RecordType = DnsRecordType.A,
                TimeoutMilliseconds = ToMilliseconds(timeout),
            }),
            _ => throw new ArgumentOutOfRangeException(nameof(check), $"Unsupported check type '{check.Type}'."),
        };

        var context = new ToolExecutionContext
        {
            Metadata = new Dictionary<string, string>
            {
                ["source"] = "monitoring",
                ["correlationId"] = check.CheckId,
            },
        };

        ToolResult toolResult;

        try
        {
            // The check timeout is the authoritative bound; a buffer protects against a tool that ignores the
            // input timeout (defense in depth on top of the execution service's own WaitAsync over tool.Timeout).
            toolResult = await _execution.ExecuteAsync(toolName, input, context, cancellationToken)
                .WaitAsync(timeout + TimeSpan.FromMilliseconds(ExecutorOuterBoundBufferMilliseconds), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Fail(check, target, MonitorErrorClass.Cancelled, MonitoringResultStatus.Cancelled,
                "Check was cancelled.", observed: null, started, DateTimeOffset.UtcNow - started);
        }
        catch (TimeoutException)
        {
            return Fail(check, target, MonitorErrorClass.Timeout, MonitoringResultStatus.Timeout,
                $"Check '{check.CheckId}' exceeded its timeout of {timeout.TotalSeconds:0.#}s.",
                observed: null, started, DateTimeOffset.UtcNow - started);
        }

        return Map(toolResult, check, target, started, toolName);
    }

    private static MonitoringResult Map(ToolResult result, MonitoringCheck check, MonitoringTarget target,
        DateTimeOffset started, string toolName)
    {
        var duration = DateTimeOffset.UtcNow - started;

        switch (result.ErrorCode)
        {
            case "Cancelled":
                return Fail(check, target, MonitorErrorClass.Cancelled, MonitoringResultStatus.Cancelled,
                    result.Error ?? "Check was cancelled.", null, started, duration);
            case "TimedOut":
                return Fail(check, target, MonitorErrorClass.Timeout, MonitoringResultStatus.Timeout,
                    result.Error ?? "Check timed out.", null, started, duration);
            case "ToolNotFound":
                return Fail(check, target, MonitorErrorClass.ExecutionError, MonitoringResultStatus.Error,
                    result.Error ?? "Monitoring tool is not registered.", null, started, duration);
        }

        if (!result.Success)
        {
            var classification = check.Type switch
            {
                MonitorCheckType.Ping when result.ErrorCode == "ResolutionFailed" => MonitorErrorClass.NameResolution,
                MonitorCheckType.DnsResolution => MonitorErrorClass.DnsResolution,
                MonitorCheckType.TcpConnectivity => MonitorErrorClass.TcpConnectivity,
                _ => MonitorErrorClass.ExecutionError,
            };

            return Fail(check, target, classification, MonitoringResultStatus.Unhealthy,
                result.Error ?? "Check failed.", result.Data, started, duration);
        }

        return check.Type switch
        {
            MonitorCheckType.Ping => MapPing(result, check, target, toolName, started, duration),
            MonitorCheckType.TcpConnectivity => MapTcp(result, check, target, toolName, started, duration),
            MonitorCheckType.DnsResolution => MapDns(result, check, target, toolName, started, duration),
            _ => Fail(check, target, MonitorErrorClass.ExecutionError, MonitoringResultStatus.Error,
                $"Unsupported check type '{check.Type}'.", null, started, duration),
        };
    }

    private static MonitoringResult MapPing(ToolResult result, MonitoringCheck check, MonitoringTarget target,
        string toolName, DateTimeOffset started, TimeSpan duration)
    {
        if (result.Data is not PingResult ping)
        {
            return Fail(check, target, MonitorErrorClass.ExecutionError, MonitoringResultStatus.Error,
                "Ping returned an unexpected result shape.", null, started, duration);
        }

        if (ping.Sent == 0)
        {
            return Fail(check, target, MonitorErrorClass.ExecutionError, MonitoringResultStatus.Error,
                "No ping probes were sent.", ping, started, duration);
        }

        // A ping that returns zero success replies but reported timeouts is genuinely "no reply" — the host
        // availability could not be confirmed via ICMP (blocked, filtered, or down — we do not guess which).
        if (ping.Received == 0 && ping.TimedOutCount > 0)
        {
            return Fail(check, target, MonitorErrorClass.Timeout, MonitoringResultStatus.Timeout,
                "ICMP echo request timed out; host availability could not be confirmed via ICMP.", ping, started, duration);
        }

        // Partial success → degraded: some probes returned, some were lost.
        if (ping.Received > 0 && ping.Received < ping.Sent)
        {
            return Ok(check, target, MonitoringResultStatus.Warning, MonitorErrorClass.Unreachable,
                $"Partial packet loss ({ping.PacketLossPercent:0}%; {ping.Received}/{ping.Sent} replies).", ping, toolName, started, duration);
        }

        if (ping.Received > 0)
        {
            return Ok(check, target, MonitoringResultStatus.Healthy, MonitorErrorClass.None,
                $"ICMP echo reply received ({ping.Received}/{ping.Sent} replies; avg {ping.AverageLatencyMilliseconds:0} ms; {ping.PacketLossPercent:0}% loss).",
                ping, toolName, started, duration);
        }

        return Fail(check, target, MonitorErrorClass.Unreachable, MonitoringResultStatus.Unhealthy,
            "ICMP echo request got no reply; host availability could not be confirmed via ICMP.", ping, started, duration);
    }

    private static MonitoringResult MapTcp(ToolResult result, MonitoringCheck check, MonitoringTarget target,
        string toolName, DateTimeOffset started, TimeSpan duration)
    {
        if (result.Data is not TcpTestResult tcp)
        {
            return Fail(check, target, MonitorErrorClass.ExecutionError, MonitoringResultStatus.Error,
                "TCP check returned an unexpected result shape.", null, started, duration);
        }

        return tcp.State switch
        {
            "Open" => Ok(check, target, MonitoringResultStatus.Healthy, MonitorErrorClass.None,
                $"TCP connection to port {tcp.Port} succeeded.", tcp, toolName, started, duration),
            "TimedOut" => Fail(check, target, MonitorErrorClass.Timeout, MonitoringResultStatus.Timeout,
                $"TCP connection to port {tcp.Port} timed out — service connectivity could not be confirmed.", tcp, started, duration),
            _ => Fail(check, target, MonitorErrorClass.TcpConnectivity, MonitoringResultStatus.Unhealthy,
                $"TCP connection to port {tcp.Port} refused/closed — service connectivity issue (does not imply the application is down).",
                tcp, started, duration),
        };
    }

    private static MonitoringResult MapDns(ToolResult result, MonitoringCheck check, MonitoringTarget target,
        string toolName, DateTimeOffset started, TimeSpan duration)
    {
        if (result.Data is not DnsLookupResult dns)
        {
            return Fail(check, target, MonitorErrorClass.DnsResolution, MonitoringResultStatus.Unhealthy,
                "DNS lookup returned an unexpected result shape.", null, started, duration);
        }

        if (!dns.Success)
        {
            return Fail(check, target, MonitorErrorClass.DnsResolution, MonitoringResultStatus.Unhealthy,
                dns.Error ?? "DNS name resolution failed.", dns, started, duration);
        }

        var addresses = dns.Records
            .Where(r => r.RecordType is "A" or "AAAA")
            .Select(r => r.Result)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var resolved = addresses.Count == 0
            ? $"{dns.Records.Count} record(s) returned."
            : string.Join(", ", addresses);

        return Ok(check, target, MonitoringResultStatus.Healthy, MonitorErrorClass.None,
            $"DNS resolved '{dns.Host}' to {resolved}", dns, toolName, started, duration);
    }

    private static int ToMilliseconds(TimeSpan timeout) =>
        Math.Clamp((int)timeout.TotalMilliseconds, 100, 60_000);

    private static MonitoringResult Ok(MonitoringCheck check, MonitoringTarget target, MonitoringResultStatus status,
        MonitorErrorClass classification, string message, object? observed, string toolName,
        DateTimeOffset started, TimeSpan duration) => new()
    {
        CheckId = check.CheckId,
        TargetId = target.Id,
        CheckType = check.Type,
        Status = status,
        ErrorClassification = classification,
        Timestamp = started,
        Duration = duration,
        SafeMessage = message,
        Observed = observed,
        Evidence = new MonitoringEvidence
        {
            Source = $"NetworkTool.{toolName}",
            Summary = message,
            Data = observed,
            Timestamp = started,
        },
        CorrelationId = check.CheckId,
    };

    private static MonitoringResult Fail(MonitoringCheck check, MonitoringTarget target, MonitorErrorClass classification,
        MonitoringResultStatus status, string message, object? observed, DateTimeOffset started, TimeSpan duration) => new()
    {
        CheckId = check.CheckId,
        TargetId = target.Id,
        CheckType = check.Type,
        Status = status,
        ErrorClassification = classification,
        Timestamp = started,
        Duration = duration,
        SafeMessage = message,
        Observed = observed,
        Evidence = new MonitoringEvidence
        {
            Source = classification switch
            {
                MonitorErrorClass.NameResolution => "NetworkTool.Resolution",
                MonitorErrorClass.DnsResolution => "NetworkTool.DnsResolution",
                MonitorErrorClass.TcpConnectivity => "NetworkTool.TcpTest",
                MonitorErrorClass.Unreachable => "NetworkTool.Ping",
                MonitorErrorClass.Timeout => "Monitoring",
                _ => "Monitoring",
            },
            Summary = message,
            Data = observed,
            Timestamp = started,
        },
        CorrelationId = check.CheckId,
    };
}