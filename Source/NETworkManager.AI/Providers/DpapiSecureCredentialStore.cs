using System.Security.Cryptography;
using System.Text;
using NETworkManager.AI.Abstractions;

namespace NETworkManager.AI.Providers;

/// <summary>
///     DPAPI-backed secure credential store (Windows). Secrets are encrypted per-user via
/// <see cref="ProtectedData"/> (CurrentUser scope) and written to a file beside the application data —
/// never in source, config JSON, or logs. Cross-platform note: this implementation is Windows-only by design;
/// tests use <c>InMemorySecureCredentialStore</c> behind the same interface.
/// </summary>
public sealed class DpapiSecureCredentialStore : ISecureCredentialStore
{
    private readonly string _directory;
    private readonly byte[] _entropy;

    public DpapiSecureCredentialStore(string directory, string? entropy = null)
    {
        if (string.IsNullOrWhiteSpace(directory))
            throw new ArgumentException("Directory must not be empty.", nameof(directory));

        _directory = directory;
        _entropy = Encoding.UTF8.GetBytes(entropy ?? "TheWiseNetwork.AI.Credentials.v1");
    }

    public Task StoreAsync(string key, string secret, CancellationToken cancellationToken = default)
    {
        Validate(key, nameof(key));

        if (string.IsNullOrEmpty(secret))
            throw new ArgumentException("Secret must not be empty.", nameof(secret));

        var encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(secret), _entropy, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(PathFor(key), encrypted);

        return Task.CompletedTask;
    }

    public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        Validate(key, nameof(key));

        var path = PathFor(key);

        if (!File.Exists(path))
            return Task.FromResult<string?>(null);

        try
        {
            var decrypted = ProtectedData.Unprotect(File.ReadAllBytes(path), _entropy, DataProtectionScope.CurrentUser);
            return Task.FromResult<string?>(Encoding.UTF8.GetString(decrypted));
        }
        catch (CryptographicException)
        {
            // unreadable on this machine/user — treat as missing
            return Task.FromResult<string?>(null);
        }
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        Validate(key, nameof(key));

        var path = PathFor(key);

        if (File.Exists(path))
            File.Delete(path);

        return Task.CompletedTask;
    }

    public Task<bool> HasCredentialAsync(string key, CancellationToken cancellationToken = default)
    {
        Validate(key, nameof(key));

        return Task.FromResult(File.Exists(PathFor(key)));
    }

    private string PathFor(string key)
    {
        Directory.CreateDirectory(_directory);
        return Path.Combine(_directory, $"{Sanitize(key)}.credential");
    }

    private static string Sanitize(string key)
    {
        var builder = new StringBuilder(key.Length);

        foreach (var c in key)
            builder.Append(char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_');

        return builder.ToString();
    }

    private static void Validate(string value, string parameter)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Key must not be empty.", parameter);
    }
}