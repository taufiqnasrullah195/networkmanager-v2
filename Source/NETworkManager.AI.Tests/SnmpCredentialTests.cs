using NETworkManager.AI.Snmp;
using Xunit;

namespace NETworkManager.AI.Tests;

public class SnmpCredentialTests
{
    [Fact]
    public void Round_trip_preserves_credential_fields()
    {
        var credential = new SnmpCredential
        {
            Version = SnmpVersion.V3,
            Username = "monitor",
            SecurityLevel = SnmpSecurityLevel.AuthPriv,
            AuthAlgorithm = SnmpAuthAlgorithm.Sha256,
            AuthPassword = "auth-secret",
            PrivAlgorithm = SnmpPrivAlgorithm.Aes,
            PrivPassword = "priv-secret",
        };

        var decoded = SnmpCredentialCodec.Decode(SnmpCredentialCodec.Encode(credential));

        Assert.NotNull(decoded);
        Assert.Equal(SnmpVersion.V3, decoded!.Version);
        Assert.Equal("monitor", decoded.Username);
        Assert.Equal(SnmpSecurityLevel.AuthPriv, decoded.SecurityLevel);
        Assert.Equal("auth-secret", decoded.AuthPassword);
        Assert.Equal("priv-secret", decoded.PrivPassword);
    }

    [Fact]
    public void Decode_null_or_garbage_returns_null()
    {
        Assert.Null(SnmpCredentialCodec.Decode(null));
        Assert.Null(SnmpCredentialCodec.Decode(""));
        Assert.Null(SnmpCredentialCodec.Decode("{ not valid json"));
    }

    [Theory]
    [InlineData(SnmpVersion.V2C, null, false)] // v2c without community is invalid
    [InlineData(SnmpVersion.V2C, "public", true)]
    public void Validate_v2c_requires_community(SnmpVersion version, string? community, bool valid)
    {
        var credential = new SnmpCredential { Version = version, Community = community };

        Assert.Equal(!valid, credential.Validate().Count > 0);
    }

    [Fact]
    public void Validate_v3_requires_username()
    {
        Assert.Single(new SnmpCredential { Version = SnmpVersion.V3, Username = null }.Validate());
        Assert.Empty(new SnmpCredential { Version = SnmpVersion.V3, Username = "monitor" }.Validate());
    }

    [Fact]
    public void Validate_v3_auth_requires_password()
    {
        var noAuth = new SnmpCredential { Version = SnmpVersion.V3, Username = "u", SecurityLevel = SnmpSecurityLevel.AuthNoPriv };
        Assert.Single(noAuth.Validate());

        var withAuth = noAuth with { AuthPassword = "secret" };
        Assert.Empty(withAuth.Validate());
    }

    [Fact]
    public void Validate_v3_auth_priv_requires_privacy_password()
    {
        var authPriv = new SnmpCredential
        {
            Version = SnmpVersion.V3,
            Username = "u",
            SecurityLevel = SnmpSecurityLevel.AuthPriv,
            AuthPassword = "auth",
        };
        Assert.Single(authPriv.Validate());

        var ok = authPriv with { PrivPassword = "priv" };
        Assert.Empty(ok.Validate());
    }

    [Fact]
    public void Check_config_requires_credential_reference()
    {
        var config = new SnmpCheckConfig { CredentialReference = null };
        Assert.Contains(config.Validate(), e => e.Contains("credential reference", StringComparison.OrdinalIgnoreCase));

        Assert.Empty(new SnmpCheckConfig { CredentialReference = "ref" }.Validate());
    }
}
