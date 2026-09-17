using NETworkManager.AI.Abstractions;
using NETworkManager.AI.Monitoring;
using NETworkManager.AI.Tests.Fakes;
using Xunit;

namespace NETworkManager.AI.Tests;

public class MonitoringProfileServiceTests
{
    private static MonitoringProfile Profile(string name = "Core Network", bool enabled = true) => new()
    {
        Id = name,
        Name = name,
        Description = "core infra",
        Enabled = enabled,
        Targets = new[]
        {
            new MonitoringTarget { Id = "gw", Name = "Gateway", IPAddress = "192.168.1.1" },
        },
        Checks = new[]
        {
            new MonitoringCheck { CheckId = "gw-ping", Type = MonitorCheckType.Ping, TargetId = "gw" },
        },
    };

    private static async Task<MonitoringProfileService> NewService(InMemoryMonitoringProfileRepository repo)
    {
        var service = new MonitoringProfileService(repo);
        await service.LoadAsync();
        return service;
    }

    [Fact]
    public async Task Create_edit_delete_profile()
    {
        var service = await NewService(new InMemoryMonitoringProfileRepository());

        var created = await service.CreateProfileAsync(Profile());
        Assert.Equal("Core Network", created.EffectiveId);
        Assert.Single(await service.GetProfilesAsync());

        var edited = await service.UpdateProfileAsync(created with { Description = "updated" });
        Assert.Equal("updated", (await service.GetProfileAsync("Core Network"))!.Description);

        Assert.True(await service.DeleteProfileAsync("Core Network"));
        Assert.Empty(await service.GetProfilesAsync());
    }

    [Fact]
    public async Task Enable_disable_profile()
    {
        var service = await NewService(new InMemoryMonitoringProfileRepository());
        await service.CreateProfileAsync(Profile(enabled: true));

        Assert.True(await service.DisableProfileAsync("Core Network"));
        Assert.False((await service.GetProfileAsync("Core Network"))!.Enabled);

        Assert.True(await service.EnableProfileAsync("Core Network"));
        Assert.True((await service.GetProfileAsync("Core Network"))!.Enabled);

        Assert.False(await service.EnableProfileAsync("missing"));
    }

    [Fact]
    public async Task Duplicate_profile_name_is_rejected()
    {
        var service = await NewService(new InMemoryMonitoringProfileRepository());
        await service.CreateProfileAsync(Profile("Core Network"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateProfileAsync(Profile("core network")));
    }

    [Fact]
    public async Task Invalid_profile_is_rejected_on_create_and_update()
    {
        var service = await NewService(new InMemoryMonitoringProfileRepository());
        var invalid = new MonitoringProfile { Name = "Bad", Enabled = true }; // no targets

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateProfileAsync(invalid));

        var created = await service.CreateProfileAsync(Profile("Good"));
        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateProfileAsync(created with { DefaultInterval = TimeSpan.FromMilliseconds(1) }));
    }

    [Fact]
    public async Task ValidateProfile_returns_clear_messages()
    {
        var service = await NewService(new InMemoryMonitoringProfileRepository());

        var errors = service.ValidateProfile(new MonitoringProfile
        {
            Name = "X",
            DefaultInterval = TimeSpan.FromMilliseconds(1),
            DefaultTimeout = TimeSpan.FromHours(1),
            MaxConcurrency = 999,
            Targets = new[] { new MonitoringTarget { Id = "t", IPAddress = "10.0.0.1" } },
            Checks = new[] { new MonitoringCheck { CheckId = "c", Type = MonitorCheckType.TcpConnectivity, TargetId = "t", Port = 0 } },
        });

        Assert.Contains(errors, e => e.Contains("Interval must be between"));
        Assert.Contains(errors, e => e.Contains("Timeout must be between"));
        Assert.Contains(errors, e => e.Contains("Maximum concurrency must be between"));
        Assert.Contains(errors, e => e.Contains("TCP port must be between"));
    }

    [Fact]
    public async Task Malformed_configuration_is_handled_safely()
    {
        // A repository that throws on load (malformed/tampered) must not crash the service.
        var repo = new ThrowingMonitoringProfileRepository();
        var service = new MonitoringProfileService(repo);

        await service.LoadAsync();

        Assert.Empty(await service.GetProfilesAsync());
        Assert.NotNull(service.LastError);
    }

    private sealed class ThrowingMonitoringProfileRepository : IMonitoringProfileRepository
    {
        public Task<MonitoringProfileCatalog?> LoadAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidDataException("malformed");

        public Task SaveAsync(MonitoringProfileCatalog catalog, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}