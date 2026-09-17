using NETworkManager.AI.Abstractions;
using NETworkManager.AI.Notifications;
using Xunit;

namespace NETworkManager.AI.Tests;

public class NotificationSecurityTests
{
    [Theory]
    [InlineData(typeof(Notification))]
    [InlineData(typeof(NotificationRequest))]
    [InlineData(typeof(NotificationInfo))]
    public void Notification_models_have_no_secret_properties(Type type)
    {
        var properties = type.GetProperties().Select(p => p.Name).ToList();

        Assert.DoesNotContain(properties, p =>
            p.Contains("Password", StringComparison.OrdinalIgnoreCase)
            || p.Contains("Secret", StringComparison.OrdinalIgnoreCase)
            || p.Contains("Token", StringComparison.OrdinalIgnoreCase)
            || p.Contains("Credential", StringComparison.OrdinalIgnoreCase)
            || p.Contains("ApiKey", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Channel_is_send_only()
    {
        var methods = typeof(INotificationChannel).GetMethods()
            .Select(m => m.Name)
            .Where(n => !n.StartsWith("get_", StringComparison.Ordinal) && !n.StartsWith("set_", StringComparison.Ordinal))
            .ToList();

        Assert.Contains(methods, m => m == "SendAsync");
        Assert.DoesNotContain(methods, m => m.Contains("Set", StringComparison.OrdinalIgnoreCase)
                                           || m.Contains("Configure", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Ai_tool_is_read_only_and_depends_only_on_query()
    {
        var ctor = typeof(NotificationHistoryTool).GetConstructors().Single();
        var param = Assert.Single(ctor.GetParameters());
        Assert.Equal(typeof(INotificationQuery), param.ParameterType);

        var declared = typeof(NotificationHistoryTool)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Where(m => m.DeclaringType == typeof(NotificationHistoryTool))
            .Select(m => m.Name);
        Assert.DoesNotContain(declared, n => n.Contains("Send", StringComparison.OrdinalIgnoreCase)
                                             || n.Contains("Record", StringComparison.OrdinalIgnoreCase));
    }
}
