namespace NETworkManager.AI.Snmp;

/// <summary>
///     Read-only SNMP transport boundary. Implementations perform SNMP GET / GETNEXT / WALK / GETBULK only — there is
///     deliberately no SET method, so the rest of the system cannot mutate a device even by mistake. The rest of
///     TheWiseNetwork depends only on this interface, never on a concrete SNMP library.
/// </summary>
public interface ISnmpProvider
{
    /// <summary>GET a list of OIDs. Returns only the variables the device answered (may be empty on a partial response).</summary>
    Task<IReadOnlyList<SnmpVariable>> GetAsync(SnmpSession session, IReadOnlyList<string> oids,
        CancellationToken cancellationToken = default);

    /// <summary>WALK a sub-tree. Returns every variable beneath the OID (GETNEXT / GETBULK loop).</summary>
    Task<IReadOnlyList<SnmpVariable>> WalkAsync(SnmpSession session, string oid,
        CancellationToken cancellationToken = default);
}
