using System.Collections.Concurrent;
using NETworkManager.AI.Abstractions;

namespace NETworkManager.AI.Tests.Fakes;

/// <summary>In-memory ISecureCredentialStore for tests — same contract as the DPAPI-backed production store.</summary>
public sealed class InMemorySecureCredentialStore : ISecureCredentialStore
{
    private readonly ConcurrentDictionary<string, string> _credentials = new(StringComparer.Ordinal);

    public Task StoreAsync(string key, string secret, CancellationToken cancellationToken = default)
    {
        _credentials[key] = secret;
        return Task.CompletedTask;
    }

    public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(_credentials.TryGetValue(key, out var secret) ? secret : null);

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        _credentials.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    public Task<bool> HasCredentialAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(_credentials.ContainsKey(key));
}