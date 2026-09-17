using NETworkManager.AI.Snmp;
using Xunit;

namespace NETworkManager.AI.Tests;

public class SnmpSecurityTests
{
    [Fact]
    public void Provider_exposes_no_set_method()
    {
        var methods = typeof(ISnmpProvider).GetMethods().Select(m => m.Name).ToList();
        Assert.DoesNotContain(methods, m => m.Contains("Set", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(methods, m => m.Equals("GetAsync", StringComparison.Ordinal));
        Assert.Contains(methods, m => m.Equals("WalkAsync", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(typeof(DeviceTelemetry))]
    [InlineData(typeof(InterfaceTelemetry))]
    [InlineData(typeof(SnmpCollectionResult))]
    public void Telemetry_models_have_no_secret_properties(Type type)
    {
        var properties = type.GetProperties().Select(p => p.Name).ToList();
        Assert.DoesNotContain(properties, p =>
            p.Contains("Community", StringComparison.OrdinalIgnoreCase)
            || p.Contains("Password", StringComparison.OrdinalIgnoreCase)
            || p.Contains("Secret", StringComparison.OrdinalIgnoreCase)
            || p.Contains("Token", StringComparison.OrdinalIgnoreCase)
            || p.Contains("Credential", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Session_carries_credential_but_is_internal_to_the_provider_boundary()
    {
        // SnmpSession holds the credential only for transport; it is not a telemetry model and is never returned
        // by the collector (which returns SnmpCollectionResult/DeviceTelemetry/InterfaceTelemetry, all secret-free).
        var telemetryTypes = new[] { typeof(SnmpCollectionResult), typeof(DeviceTelemetry), typeof(InterfaceTelemetry) };
        foreach (var type in telemetryTypes)
        {
            Assert.DoesNotContain(type.GetProperties(), p => p.PropertyType == typeof(SnmpSession));
            Assert.DoesNotContain(type.GetProperties(), p => p.PropertyType == typeof(SnmpCredential));
        }
    }
}
