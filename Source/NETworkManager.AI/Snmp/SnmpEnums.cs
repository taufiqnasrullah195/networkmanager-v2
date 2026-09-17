namespace NETworkManager.AI.Snmp;

/// <summary>SNMP protocol version. V3 is supported architecturally but requires a secure credential (see <see cref="SnmpCredential"/>).</summary>
public enum SnmpVersion
{
    V1 = 0,
    V2C = 1,
    V3 = 2,
}

/// <summary>What a single SNMP monitoring check collects.</summary>
public enum SnmpCollectionMode
{
    /// <summary>System group only (sysName, sysDescr, sysObjectID, sysUpTime).</summary>
    System = 0,

    /// <summary>Interface table only (IF-MIB counters/status).</summary>
    Interfaces = 1,

    /// <summary>Both system group and interface table.</summary>
    SystemAndInterfaces = 2,
}

/// <summary>Deterministic outcome of one SNMP collection attempt. Never fabricated.</summary>
public enum SnmpCollectionStatus
{
    Success = 0,
    Partial = 1,
    Failed = 2,
    Timeout = 3,
    Unavailable = 4,
}

/// <summary>SNMPv3 security level.</summary>
public enum SnmpSecurityLevel
{
    NoAuthNoPriv = 0,
    AuthNoPriv = 1,
    AuthPriv = 2,
}

/// <summary>SNMPv3 authentication algorithm.</summary>
public enum SnmpAuthAlgorithm
{
    None = 0,
    Md5 = 1,
    Sha1 = 2,
    Sha256 = 3,
    Sha384 = 4,
    Sha512 = 5,
}

/// <summary>SNMPv3 privacy (encryption) algorithm.</summary>
public enum SnmpPrivAlgorithm
{
    None = 0,
    Des = 1,
    Aes = 2,
    Aes192 = 3,
    Aes256 = 4,
}

/// <summary>Normalized administrative interface state (RFC 2863 ifAdminStatus).</summary>
public enum SnmpInterfaceAdminStatus
{
    Up = 1,
    Down = 2,
    Testing = 3,
    Unknown = 4,
    NotPresent = 6,
    LowerLayerDown = 7,
}

/// <summary>Normalized operational interface state (RFC 2863 ifOperStatus).</summary>
public enum SnmpInterfaceOperStatus
{
    Up = 1,
    Down = 2,
    Testing = 3,
    Unknown = 4,
    Dormant = 5,
    NotPresent = 6,
    LowerLayerDown = 7,
}

/// <summary>Typed cause of a failed SNMP operation — lets the collector map to a status deterministically.</summary>
public enum SnmpErrorKind
{
    Timeout = 0,
    Authentication = 1,
    Unavailable = 2,
    InvalidOid = 3,
    Cancelled = 4,
    Other = 5,
}
