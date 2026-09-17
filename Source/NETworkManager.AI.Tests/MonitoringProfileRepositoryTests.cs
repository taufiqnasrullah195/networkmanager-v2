using NETworkManager.AI.Monitoring;
using Xunit;

namespace NETworkManager.AI.Tests;

public class MonitoringProfileRepositoryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "twn-monitoring-tests-" + Guid.NewGuid().ToString("N"));

    private static MonitoringProfile Sample() => new()
    {
        Id = "core",
        Name = "Core Network",
        Description = "core",
        Enabled = true,
        Targets = new[] { new MonitoringTarget { Id = "gw", Name = "Gateway", IPAddress = "192.168.1.1" } },
        Checks = new[]
        {
            new MonitoringCheck { CheckId = "gw-ping", Type = MonitorCheckType.Ping, TargetId = "gw" },
            new MonitoringCheck { CheckId = "gw-tcp", Type = MonitorCheckType.TcpConnectivity, TargetId = "gw", Port = 443 },
        },
    };

    [Fact]
    public async Task Round_trips_a_catalog()
    {
        var repo = new JsonMonitoringProfileRepository(CatalogPath());

        var catalog = new MonitoringProfileCatalog { Version = 1, Profiles = new[] { Sample() } };
        await repo.SaveAsync(catalog);

        var loaded = await repo.LoadAsync();
        Assert.NotNull(loaded);
        Assert.Equal(1, loaded!.Version);
        var profile = Assert.Single(loaded.Profiles);
        Assert.Equal("core", profile.Id);
        Assert.Equal(2, profile.Checks.Count);
    }

    [Fact]
    public async Task Missing_file_returns_null()
    {
        var repo = new JsonMonitoringProfileRepository(CatalogPath());
        Assert.Null(await repo.LoadAsync());
    }

    [Fact]
    public async Task Tampered_file_throws_invalid_data()
    {
        var repo = new JsonMonitoringProfileRepository(CatalogPath());
        await repo.SaveAsync(new MonitoringProfileCatalog { Profiles = new[] { Sample() } });

        // Corrupt the checksum so the payload no longer matches.
        var text = await File.ReadAllTextAsync(CatalogPath());
        var corrupted = text.Replace("\"checksum\":\"", "\"checksum\":\"0000");
        await File.WriteAllTextAsync(CatalogPath(), corrupted);

        await Assert.ThrowsAsync<InvalidDataException>(() => repo.LoadAsync());
    }

    [Fact]
    public async Task Migrates_legacy_single_profile_file()
    {
        var legacyPath = Path.Combine(_dir, "monitoring-profile.json");
        var catalogPath = Path.Combine(_dir, "monitoring-profiles.json");

        // Write the Step 9 single-profile file via the old store.
        new MonitoringProfileStore(legacyPath).Save(Sample());

        var repo = new JsonMonitoringProfileRepository(catalogPath, legacyPath);
        var catalog = await repo.LoadAsync();

        Assert.NotNull(catalog);
        Assert.Single(catalog!.Profiles);

        // Migration should have written the catalog file for future loads.
        Assert.True(File.Exists(catalogPath));
    }

    private string CatalogPath() => Path.Combine(_dir, "monitoring-profiles.json");

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            // best-effort cleanup
        }
    }
}