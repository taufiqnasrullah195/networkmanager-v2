using System;
using System.IO;
using NETworkManager.AI.Abstractions;
using NETworkManager.AI.Providers;
using NETworkManager.AI.Snmp;

namespace NETworkManager;

/// <summary>
///     Composition root for the read-only SNMP telemetry subsystem (Step 13). Builds the Lextm-backed provider, the
///     normalizer, the collector (with the secure credential store + persistence repository), and exposes the secure
///     credential store for the credential-management UI. No SNMP SET path exists anywhere in this composition.
/// </summary>
public static class SnmpComposition
{
    private static DpapiSecureCredentialStore? _credentialStore;
    private static SnmpTelemetryCollector? _collector;

    /// <summary>DPAPI-backed credential store keyed by credential reference (community strings / SNMPv3 secrets).</summary>
    public static ISecureCredentialStore CredentialStore =>
        _credentialStore ??= new DpapiSecureCredentialStore(CredentialDirectory);

    /// <summary>The telemetry collector used by the monitoring engine's SNMP check executor.</summary>
    public static SnmpTelemetryCollector Collector =>
        _collector ??= new SnmpTelemetryCollector(
            new SharpSnmpProvider(),
            new SnmpNormalizer(),
            CredentialStore,
            PersistenceComposition.SnmpTelemetryRepository);

    private static string CredentialDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NETworkManager",
        "AI",
        "snmp-credentials");
}
