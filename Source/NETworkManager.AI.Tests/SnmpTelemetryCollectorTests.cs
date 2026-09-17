using NETworkManager.AI.Abstractions;
using NETworkManager.AI.Snmp;
using NETworkManager.AI.Tests.Fakes;
using Xunit;

namespace NETworkManager.AI.Tests;

public class SnmpTelemetryCollectorTests
{
    private const string Reference = "network-device-01";
    private const string Community = "public-readonly";

    private static SnmpCheckConfig Config(SnmpVersion version = SnmpVersion.V2C, SnmpCollectionMode mode = SnmpCollectionMode.SystemAndInterfaces) => new()
    {
        Version = version,
        CredentialReference = Reference,
        CollectionMode = mode,
    };

    private static SnmpTelemetryCollector BuildCollector(FakeSnmpProvider provider, InMemorySecureCredentialStore store,
        ISnmpTelemetryRepository? repository = null) =>
        new(provider, new SnmpNormalizer(), store, repository);

    private static async Task<InMemorySecureCredentialStore> WithV2CredentialAsync(InMemorySecureCredentialStore store) 
    {
        await store.StoreAsync(Reference, SnmpCredentialCodec.Encode(new SnmpCredential { Version = SnmpVersion.V2C, Community = Community }));
        return store;
    }

    [Fact]
    public async Task Collects_system_and_interfaces()
    {
        var store = new InMemorySecureCredentialStore();
        await WithV2CredentialAsync(store);

        var provider = new FakeSnmpProvider
        {
            OnGet = (_, _) => SnmpTestData.System(),
            OnWalk = oid => oid == SnmpNormalizer.IfTable
                ? SnmpTestData.Interfaces((1, "1", "1000", "2000"))
                : SnmpTestData.IfX((1, "5000000000", "6000000000")),
        };

        var collector = BuildCollector(provider, store);
        var result = await collector.CollectAsync("sw01", "10.0.0.1", Config());

        Assert.Equal(SnmpCollectionStatus.Success, result.Status);
        Assert.NotNull(result.Device);
        Assert.Equal("SW01", result.Device!.SysName);
        Assert.Equal(1, result.Device.InterfaceCount);
        Assert.Single(result.Interfaces);
        Assert.Equal(5_000_000_000ul, result.Interfaces[0].InOctets);
    }

    [Fact]
    public async Task Timeout_maps_to_timeout_status()
    {
        var store = new InMemorySecureCredentialStore();
        await WithV2CredentialAsync(store);

        var provider = new FakeSnmpProvider { ThrowOnGet = new SnmpException(SnmpErrorKind.Timeout, "timed out") };
        var result = await BuildCollector(provider, store).CollectAsync("sw01", "10.0.0.1", Config());

        Assert.Equal(SnmpCollectionStatus.Timeout, result.Status);
    }

    [Fact]
    public async Task Authentication_failure_maps_to_failed()
    {
        var store = new InMemorySecureCredentialStore();
        await WithV2CredentialAsync(store);

        var provider = new FakeSnmpProvider { ThrowOnGet = new SnmpException(SnmpErrorKind.Authentication, "auth failed") };
        var result = await BuildCollector(provider, store).CollectAsync("sw01", "10.0.0.1", Config());

        Assert.Equal(SnmpCollectionStatus.Failed, result.Status);
    }

    [Fact]
    public async Task Unreachable_maps_to_unavailable()
    {
        var store = new InMemorySecureCredentialStore();
        await WithV2CredentialAsync(store);

        var provider = new FakeSnmpProvider { ThrowOnGet = new SnmpException(SnmpErrorKind.Unavailable, "unreachable") };
        var result = await BuildCollector(provider, store).CollectAsync("sw01", "10.0.0.1", Config());

        Assert.Equal(SnmpCollectionStatus.Unavailable, result.Status);
    }

    [Fact]
    public async Task Missing_credential_reference_returns_failed()
    {
        var store = new InMemorySecureCredentialStore(); // nothing stored
        var provider = new FakeSnmpProvider();

        var result = await BuildCollector(provider, store).CollectAsync("sw01", "10.0.0.1", Config());

        Assert.Equal(SnmpCollectionStatus.Failed, result.Status);
        Assert.Contains("not configured", result.Errors.Single(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Community, string.Join(" ", result.Errors));
    }

    [Fact]
    public async Task Version_mismatch_returns_failed()
    {
        var store = new InMemorySecureCredentialStore();
        await store.StoreAsync(Reference, SnmpCredentialCodec.Encode(new SnmpCredential
        {
            Version = SnmpVersion.V3,
            Username = "monitor",
            SecurityLevel = SnmpSecurityLevel.NoAuthNoPriv,
        }));

        var result = await BuildCollector(new FakeSnmpProvider(), store).CollectAsync("sw01", "10.0.0.1", Config(SnmpVersion.V2C));

        Assert.Equal(SnmpCollectionStatus.Failed, result.Status);
        Assert.Contains("V3", result.Errors.Single());
    }

    [Fact]
    public async Task Invalid_credential_returns_failed()
    {
        var store = new InMemorySecureCredentialStore();
        await store.StoreAsync(Reference, SnmpCredentialCodec.Encode(new SnmpCredential { Version = SnmpVersion.V2C })); // no community

        var result = await BuildCollector(new FakeSnmpProvider(), store).CollectAsync("sw01", "10.0.0.1", Config());

        Assert.Equal(SnmpCollectionStatus.Failed, result.Status);
        Assert.Contains("community", result.Errors.Single(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Partial_response_when_interfaces_empty()
    {
        var store = new InMemorySecureCredentialStore();
        await WithV2CredentialAsync(store);

        var provider = new FakeSnmpProvider
        {
            OnGet = (_, _) => SnmpTestData.System(),
            OnWalk = _ => Array.Empty<SnmpVariable>(),
        };

        var result = await BuildCollector(provider, store).CollectAsync("sw01", "10.0.0.1", Config());

        Assert.Equal(SnmpCollectionStatus.Partial, result.Status);
        Assert.NotNull(result.Device);
        Assert.Empty(result.Interfaces);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public async Task Interfaces_only_mode_does_not_get_system()
    {
        var store = new InMemorySecureCredentialStore();
        await WithV2CredentialAsync(store);

        var provider = new FakeSnmpProvider
        {
            OnWalk = oid => oid == SnmpNormalizer.IfTable
                ? SnmpTestData.Interfaces((1, "1", "1000", "2000"))
                : Array.Empty<SnmpVariable>(),
        };

        var result = await BuildCollector(provider, store).CollectAsync("sw01", "10.0.0.1", Config(mode: SnmpCollectionMode.Interfaces));

        Assert.Equal(SnmpCollectionStatus.Success, result.Status);
        Assert.Null(result.Device);
        Assert.Single(result.Interfaces);
        Assert.Empty(provider.GetOids);
    }

    [Fact]
    public async Task Community_never_appears_in_telemetry_or_errors()
    {
        var store = new InMemorySecureCredentialStore();
        await WithV2CredentialAsync(store);

        var provider = new FakeSnmpProvider
        {
            OnGet = (_, _) => SnmpTestData.System(),
            OnWalk = oid => oid == SnmpNormalizer.IfTable
                ? SnmpTestData.Interfaces((1, "1", "1000", "2000"))
                : Array.Empty<SnmpVariable>(),
        };

        var result = await BuildCollector(provider, store).CollectAsync("sw01", "10.0.0.1", Config());

        var everything = string.Join(" ", result.Errors) + result.Device?.SysName + result.Device?.SysDescription +
                         result.Device?.SysObjectId + string.Join(" ", result.Interfaces.Select(i => i.Name));
        Assert.DoesNotContain(Community, everything, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Successful_collection_is_persisted()
    {
        var store = new InMemorySecureCredentialStore();
        await WithV2CredentialAsync(store);
        var repository = new FakeSnmpTelemetryRepository();

        var provider = new FakeSnmpProvider
        {
            OnGet = (_, _) => SnmpTestData.System(),
            OnWalk = oid => oid == SnmpNormalizer.IfTable
                ? SnmpTestData.Interfaces((1, "1", "1000", "2000"))
                : Array.Empty<SnmpVariable>(),
        };

        await BuildCollector(provider, store, repository).CollectAsync("sw01", "10.0.0.1", Config());

        Assert.Single(repository.Recorded);
        Assert.Equal("sw01", repository.Recorded[0].TargetId);
    }
}
