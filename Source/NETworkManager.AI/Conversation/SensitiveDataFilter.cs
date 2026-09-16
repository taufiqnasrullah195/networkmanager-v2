using System.Text.RegularExpressions;

namespace NETworkManager.AI.Conversation;

/// <summary>Redacts sensitive values before content is sent to an AI provider.</summary>
public interface ISensitiveDataFilter
{
    string Redact(string? input);
}

/// <summary>
///     Default filter: redacts bearer tokens and key-like assignments (api key, secret, password, token, …).
///     Labels are preserved so the redaction is visible; values are replaced with <c>***</c>.
/// </summary>
public sealed class DefaultSensitiveDataFilter : ISensitiveDataFilter
{
    private static readonly Regex SecretPattern = new(
        @"(?<label>bearer\s+|api[_-]?key\s*[:=]\s*|api[_-]?secret\s*[:=]\s*|secret\s*[:=]\s*|password\s*[:=]\s*|passwd\s*[:=]\s*|token\s*[:=]\s*|auth[_-]?token\s*[:=]\s*)(?<value>[^\s,;]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public string Redact(string? input)
    {
        if (string.IsNullOrEmpty(input))
            return input ?? string.Empty;

        return SecretPattern.Replace(input, m => m.Groups["label"].Value + "***");
    }
}