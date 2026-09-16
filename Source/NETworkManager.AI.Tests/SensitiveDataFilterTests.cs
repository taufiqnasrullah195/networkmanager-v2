using NETworkManager.AI.Conversation;
using Xunit;

namespace NETworkManager.AI.Tests;

public class SensitiveDataFilterTests
{
    private readonly DefaultSensitiveDataFilter _filter = new();

    [Fact]
    public void Redacts_password_assignment()
    {
        var result = _filter.Redact("login with password=hunter2 now");

        Assert.DoesNotContain("hunter2", result);
        Assert.Contains("password=***", result);
    }

    [Fact]
    public void Redacts_bearer_token()
    {
        var result = _filter.Redact("Authorization: Bearer abc.def.123");

        Assert.DoesNotContain("abc.def.123", result);
        Assert.Contains("Bearer ***", result);
    }

    [Fact]
    public void Redacts_api_key_and_secret()
    {
        var result = _filter.Redact("api_key: sk-12345 and secret= abc");

        Assert.DoesNotContain("sk-12345", result);
        Assert.DoesNotContain("abc", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Preserves_normal_text()
    {
        const string text = "Why is my Internet not working? Ping 192.168.1.1 please.";

        Assert.Equal(text, _filter.Redact(text));
    }

    [Fact]
    public void Null_and_empty_are_safe()
    {
        Assert.Equal(string.Empty, _filter.Redact(null));
        Assert.Equal(string.Empty, _filter.Redact(""));
    }
}