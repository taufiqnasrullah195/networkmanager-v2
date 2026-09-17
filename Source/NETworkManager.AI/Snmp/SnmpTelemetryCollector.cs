using NETworkManager.AI.Abstractions;

namespace NETworkManager.AI.Snmp;

/// <summary>
///     Orchestrates a read-only SNMP collection: resolves the credential reference, establishes a session, performs
///     GET/WALK, normalizes the results, and returns structured telemetry with a deterministic status. Produces only
///     evidence — never AI conclusions, and never a SET.
/// </summary>
public interface ISnmpTelemetryCollector
{
    Task<SnmpCollectionResult> CollectAsync(string targetId, string host, SnmpCheckConfig config,
        CancellationToken cancellationToken = default);
}

public sealed class SnmpTelemetryCollector : ISnmpTelemetryCollector
{
    private static readonly string[] SystemOids = { SnmpNormalizer.SysDescr, SnmpNormalizer.SysObjectId, SnmpNormalizer.SysUpTime, SnmpNormalizer.SysName };

    private readonly ISnmpProvider _provider;
    private readonly ISnmpNormalizer _normalizer;
    private readonly ISecureCredentialStore _credentialStore;
    private readonly ISnmpTelemetryRepository? _repository;

    public SnmpTelemetryCollector(ISnmpProvider provider, ISnmpNormalizer normalizer, ISecureCredentialStore credentialStore,
        ISnmpTelemetryRepository? repository = null)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _normalizer = normalizer ?? throw new ArgumentNullException(nameof(normalizer));
        _credentialStore = credentialStore ?? throw new ArgumentNullException(nameof(credentialStore));
        _repository = repository;
    }

    public async Task<SnmpCollectionResult> CollectAsync(string targetId, string host, SnmpCheckConfig config,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(targetId))
            throw new ArgumentException("Target id must not be empty.", nameof(targetId));

        if (string.IsNullOrWhiteSpace(host))
            throw new ArgumentException("Host must not be empty.", nameof(host));

        ArgumentNullException.ThrowIfNull(config);

        var started = DateTimeOffset.UtcNow;

        if (cancellationToken.IsCancellationRequested)
            return Failure(targetId, host, started, SnmpCollectionStatus.Failed, ["Collection was cancelled."]);

        // Resolve the credential reference from secure storage. The secret never enters telemetry/logs.
        var credential = SnmpCredentialCodec.Decode(await _credentialStore.GetAsync(config.CredentialReference!, cancellationToken).ConfigureAwait(false));

        if (credential is null)
        {
            return Failure(targetId, host, started, SnmpCollectionStatus.Failed,
                [$"SNMP credential '{config.CredentialReference}' is not configured."]);
        }

        var credentialErrors = credential.Validate();
        if (credentialErrors.Count > 0)
        {
            return Failure(targetId, host, started, SnmpCollectionStatus.Failed,
                credentialErrors.Select(e => $"SNMP credential is invalid: {e}").ToList());
        }

        if (credential.Version != config.Version)
        {
            return Failure(targetId, host, started, SnmpCollectionStatus.Failed,
                [$"SNMP credential is for {credential.Version} but the check is configured for {config.Version}."]);
        }

        var session = new SnmpSession
        {
            Host = host,
            Port = config.Port,
            Timeout = config.Timeout ?? TimeSpan.FromSeconds(5),
            Retries = config.Retries,
            Credential = credential,
        };

        try
        {
            var (device, interfaces, errors) = await CollectCoreAsync(targetId, host, config, session, cancellationToken).ConfigureAwait(false);
            var responseTime = DateTimeOffset.UtcNow - started;

            var hasData = device is not null || interfaces.Count > 0;
            var status = errors.Count == 0
                ? SnmpCollectionStatus.Success
                : (hasData ? SnmpCollectionStatus.Partial : SnmpCollectionStatus.Failed);

            var finalDevice = device is null
                ? null
                : device with { InterfaceCount = interfaces.Count, CollectionStatus = status, CollectionErrors = errors };

            var result = new SnmpCollectionResult
            {
                TargetId = targetId,
                Host = host,
                Timestamp = started,
                Status = status,
                Device = finalDevice,
                Interfaces = interfaces,
                Errors = errors,
                ResponseTime = responseTime,
            };

            // Persist evidence best-effort — a persistence failure must never fail the telemetry check.
            if (_repository is not null && status is SnmpCollectionStatus.Success or SnmpCollectionStatus.Partial)
            {
                try
                {
                    await _repository.RecordAsync(result, cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    // Swallow: monitoring continues; persistence health is surfaced separately.
                }
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            return Failure(targetId, host, started, SnmpCollectionStatus.Failed, ["Collection was cancelled."]);
        }
        catch (SnmpException ex)
        {
            return Failure(targetId, host, started, Map(ex.Kind), [ex.Message]);
        }
    }

    private async Task<(DeviceTelemetry? Device, IReadOnlyList<InterfaceTelemetry> Interfaces, IReadOnlyList<string> Errors)> CollectCoreAsync(
        string targetId, string host, SnmpCheckConfig config, SnmpSession session, CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        DeviceTelemetry? device = null;
        IReadOnlyList<InterfaceTelemetry> interfaces = Array.Empty<InterfaceTelemetry>();
        var timestamp = DateTimeOffset.UtcNow;

        if (config.CollectionMode is SnmpCollectionMode.System or SnmpCollectionMode.SystemAndInterfaces)
        {
            var systemVars = await _provider.GetAsync(session, SystemOids, cancellationToken).ConfigureAwait(false);

            if (systemVars.Count == 0)
                errors.Add("No system information (sysName/sysDescr/sysObjectID/sysUpTime) returned.");
            else
                device = _normalizer.NormalizeSystem(targetId, host, timestamp, systemVars, reachable: true,
                    responseTime: TimeSpan.Zero);
        }

        if (config.CollectionMode is SnmpCollectionMode.Interfaces or SnmpCollectionMode.SystemAndInterfaces)
        {
            var ifTable = await _provider.WalkAsync(session, SnmpNormalizer.IfTable, cancellationToken).ConfigureAwait(false);
            var ifXTable = await _provider.WalkAsync(session, SnmpNormalizer.IfXTable, cancellationToken).ConfigureAwait(false);
            var combined = ifTable.Concat(ifXTable).ToList();

            interfaces = _normalizer.NormalizeInterfaces(targetId, timestamp, combined);

            if (interfaces.Count == 0)
                errors.Add("No interface table (IF-MIB) returned.");
        }

        return (device, interfaces, errors);
    }

    private static SnmpCollectionStatus Map(SnmpErrorKind kind) => kind switch
    {
        SnmpErrorKind.Timeout => SnmpCollectionStatus.Timeout,
        SnmpErrorKind.Unavailable => SnmpCollectionStatus.Unavailable,
        SnmpErrorKind.Authentication => SnmpCollectionStatus.Failed,
        SnmpErrorKind.InvalidOid => SnmpCollectionStatus.Failed,
        SnmpErrorKind.Cancelled => SnmpCollectionStatus.Failed,
        _ => SnmpCollectionStatus.Failed,
    };

    private static SnmpCollectionResult Failure(string targetId, string host, DateTimeOffset started,
        SnmpCollectionStatus status, IReadOnlyList<string> errors) => new()
    {
        TargetId = targetId,
        Host = host,
        Timestamp = started,
        Status = status,
        Errors = errors,
        ResponseTime = DateTimeOffset.UtcNow - started,
    };
}
