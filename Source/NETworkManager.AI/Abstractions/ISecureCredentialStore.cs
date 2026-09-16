namespace NETworkManager.AI.Abstractions;

/// <summary>
///     OS-backed secure credential storage for AI provider authentication. Secrets are stored encrypted
///     (implementation-defined, e.g. DPAPI / Windows Credential Manager) and are never logged or displayed.
/// </summary>
public interface ISecureCredentialStore
{
    Task StoreAsync(string key, string secret, CancellationToken cancellationToken = default);

    /// <summary>Returns the stored secret, or <c>null</c> when no credential exists for the key.</summary>
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);

    Task RemoveAsync(string key, CancellationToken cancellationToken = default);

    Task<bool> HasCredentialAsync(string key, CancellationToken cancellationToken = default);
}