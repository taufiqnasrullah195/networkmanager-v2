using System.Net;
using System.Net.Sockets;
using Lextm.SharpSnmpLib;
using Lextm.SharpSnmpLib.Messaging;
using Lextm.SharpSnmpLib.Security;

namespace NETworkManager.AI.Snmp;

/// <summary>
///     Read-only SNMP provider backed by Lextm.SharpSnmpLib (the same library the upstream NETworkManager SNMP tool
///     uses). Exposes only GET / WALK (GETNEXT/GETBULK) — there is no SET. v1, v2c, and v3 are supported. Timeout is
///     enforced per attempt via a linked cancellation token; retries are bounded and only occur on timeout.
/// </summary>
public sealed class SharpSnmpProvider : ISnmpProvider
{
    public async Task<IReadOnlyList<SnmpVariable>> GetAsync(SnmpSession session, IReadOnlyList<string> oids,
        CancellationToken cancellationToken = default)
    {
        var endpoint = await ResolveEndpointAsync(session, cancellationToken).ConfigureAwait(false);
        var variables = oids.Select(oid => new Variable(new ObjectIdentifier(oid))).ToList();

        return await WithRetryAsync(session, cancellationToken,
            ct => GetCoreAsync(endpoint, session, variables, ct)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SnmpVariable>> WalkAsync(SnmpSession session, string oid,
        CancellationToken cancellationToken = default)
    {
        var endpoint = await ResolveEndpointAsync(session, cancellationToken).ConfigureAwait(false);
        var table = new ObjectIdentifier(oid);

        return await WithRetryAsync(session, cancellationToken,
            ct => WalkCoreAsync(endpoint, session, table, ct)).ConfigureAwait(false);
    }

    private static async Task<IPEndPoint> ResolveEndpointAsync(SnmpSession session, CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(session.Host, out var ip))
            return new IPEndPoint(ip, session.Port);

        var addresses = await Dns.GetHostAddressesAsync(session.Host, cancellationToken).ConfigureAwait(false);
        var address = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                      ?? addresses.FirstOrDefault();

        if (address is null)
            throw new SnmpException(SnmpErrorKind.Unavailable, $"Could not resolve host '{session.Host}'.");

        return new IPEndPoint(address, session.Port);
    }

    private async Task<IReadOnlyList<SnmpVariable>> WithRetryAsync(SnmpSession session, CancellationToken cancellationToken,
        Func<CancellationToken, Task<IReadOnlyList<SnmpVariable>>> operation)
    {
        var attempts = session.Retries + 1;
        Exception? last = null;

        for (var attempt = 0; attempt < attempts; attempt++)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(session.Timeout);

            try
            {
                return await operation(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw new SnmpException(SnmpErrorKind.Cancelled, "SNMP operation was cancelled.");
            }
            catch (OperationCanceledException)
            {
                last = new SnmpException(SnmpErrorKind.Timeout,
                    $"SNMP request to '{session.Host}' timed out after {session.Timeout.TotalSeconds:0.#}s.");
            }
            catch (SocketException)
            {
                throw new SnmpException(SnmpErrorKind.Unavailable, $"SNMP device '{session.Host}' is unreachable.");
            }
            catch (SnmpException ex) when (ex.Kind == SnmpErrorKind.Timeout)
            {
                last = ex;
            }
        }

        throw last ?? new SnmpException(SnmpErrorKind.Timeout, $"SNMP request to '{session.Host}' timed out.");
    }

    private static async Task<IReadOnlyList<SnmpVariable>> GetCoreAsync(IPEndPoint endpoint, SnmpSession session,
        IReadOnlyList<Variable> variables, CancellationToken cancellationToken)
    {
        return session.Credential.Version switch
        {
            SnmpVersion.V3 => await GetV3Async(endpoint, session, variables, cancellationToken).ConfigureAwait(false),
            _ => await GetV1V2Async(endpoint, session, variables, cancellationToken).ConfigureAwait(false),
        };
    }

    private static async Task<IReadOnlyList<SnmpVariable>> GetV1V2Async(IPEndPoint endpoint, SnmpSession session,
        IReadOnlyList<Variable> variables, CancellationToken cancellationToken)
    {
        var version = session.Credential.Version == SnmpVersion.V1 ? VersionCode.V1 : VersionCode.V2;
        var community = new OctetString(session.Credential.Community ?? string.Empty);

        var message = new GetRequestMessage(Messenger.NextMessageId, version, community, variables.ToList());
        var response = await message.GetResponseAsync(endpoint, cancellationToken).ConfigureAwait(false);
        var pdu = response.Pdu();

        if (pdu.ErrorStatus.ToInt32() != 0)
            throw new SnmpException(SnmpErrorKind.Other, $"SNMP device returned error '{pdu.ErrorStatus.ToErrorCode()}'.");

        return pdu.Variables.Select(v => new SnmpVariable(v.Id.ToString(), v.Data.ToString())).ToList();
    }

    private static async Task<IReadOnlyList<SnmpVariable>> GetV3Async(IPEndPoint endpoint, SnmpSession session,
        IReadOnlyList<Variable> variables, CancellationToken cancellationToken)
    {
        var username = new OctetString(session.Credential.Username ?? string.Empty);
        var discovery = Messenger.GetNextDiscovery(SnmpType.GetRequestPdu);
        var report = await discovery.GetResponseAsync(endpoint, cancellationToken).ConfigureAwait(false);
        var privacy = GetPrivacyProvider(session.Credential);

        var message = new GetRequestMessage(VersionCode.V3, Messenger.NextMessageId, Messenger.NextMessageId,
            username, OctetString.Empty, variables.ToList(), privacy, Messenger.MaxMessageSize, report);
        var response = await message.GetResponseAsync(endpoint, cancellationToken).ConfigureAwait(false);
        var pdu = response.Pdu();

        if (pdu.ErrorStatus.ToInt32() != 0)
            throw new SnmpException(SnmpErrorKind.Other, $"SNMP device returned error '{pdu.ErrorStatus.ToErrorCode()}'.");

        ThrowIfV3Report(response);

        return pdu.Variables.Select(v => new SnmpVariable(v.Id.ToString(), v.Data.ToString())).ToList();
    }

    private static async Task<IReadOnlyList<SnmpVariable>> WalkCoreAsync(IPEndPoint endpoint, SnmpSession session,
        ObjectIdentifier table, CancellationToken cancellationToken)
    {
        return session.Credential.Version switch
        {
            SnmpVersion.V3 => await WalkV3Async(endpoint, session, table, cancellationToken).ConfigureAwait(false),
            _ => await WalkV1V2Async(endpoint, session, table, cancellationToken).ConfigureAwait(false),
        };
    }

    private static async Task<IReadOnlyList<SnmpVariable>> WalkV1V2Async(IPEndPoint endpoint, SnmpSession session,
        ObjectIdentifier table, CancellationToken cancellationToken)
    {
        var version = session.Credential.Version == SnmpVersion.V1 ? VersionCode.V1 : VersionCode.V2;
        var community = new OctetString(session.Credential.Community ?? string.Empty);
        var results = new List<Variable>();
        var seed = new Variable(table);
        var subTreeMask = $"{table}.";

        do
        {
            var message = new GetNextRequestMessage(Messenger.NextRequestId, version, community,
                new List<Variable> { new(seed.Id) });
            var response = await message.GetResponseAsync(endpoint, cancellationToken).ConfigureAwait(false);
            var pdu = response.Pdu();

            if (pdu.ErrorStatus.ToErrorCode() == ErrorCode.NoSuchName)
                break;

            if (pdu.ErrorStatus.ToInt32() != 0)
                throw new SnmpException(SnmpErrorKind.Other, $"SNMP device returned error '{pdu.ErrorStatus.ToErrorCode()}'.");

            var variable = pdu.Variables[0];

            if (variable.Id == table)
                continue;

            if (variable.Data.TypeCode == SnmpType.EndOfMibView)
                break;

            if (!variable.Id.ToString().StartsWith(subTreeMask, StringComparison.Ordinal))
                break;

            results.Add(variable);
            seed = variable;
        } while (!cancellationToken.IsCancellationRequested);

        return results.Select(v => new SnmpVariable(v.Id.ToString(), v.Data.ToString())).ToList();
    }

    private static async Task<IReadOnlyList<SnmpVariable>> WalkV3Async(IPEndPoint endpoint, SnmpSession session,
        ObjectIdentifier table, CancellationToken cancellationToken)
    {
        var username = new OctetString(session.Credential.Username ?? string.Empty);
        var privacy = GetPrivacyProvider(session.Credential);
        var results = new List<Variable>();
        var seed = new Variable(table);
        var subTreeMask = $"{table}.";
        var discovery = Messenger.GetNextDiscovery(SnmpType.GetRequestPdu);
        ISnmpMessage report = await discovery.GetResponseAsync(endpoint, cancellationToken).ConfigureAwait(false);
        var breakLoop = false;

        do
        {
            var request = new GetBulkRequestMessage(VersionCode.V3, Messenger.NextMessageId, Messenger.NextRequestId,
                username, OctetString.Empty, 0, 10, new List<Variable> { new(seed.Id) }, privacy,
                Messenger.MaxMessageSize, report);
            var response = await request.GetResponseAsync(endpoint, cancellationToken).ConfigureAwait(false);
            var pdu = response.Pdu();

            if (pdu.ErrorStatus.ToInt32() != 0)
                throw new SnmpException(SnmpErrorKind.Other, $"SNMP device returned error '{pdu.ErrorStatus.ToErrorCode()}'.");

            ThrowIfV3Report(response);

            if (response is ReportMessage reportMessage && pdu.Variables.Count > 0 &&
                pdu.Variables[0].Id == Messenger.NotInTimeWindow)
            {
                request = new GetBulkRequestMessage(VersionCode.V3, Messenger.NextMessageId, Messenger.NextRequestId,
                    username, OctetString.Empty, 0, 10, new List<Variable> { new(seed.Id) }, privacy,
                    Messenger.MaxMessageSize, response);
                response = await request.GetResponseAsync(endpoint, cancellationToken).ConfigureAwait(false);
                pdu = response.Pdu();

                if (pdu.ErrorStatus.ToInt32() != 0)
                    throw new SnmpException(SnmpErrorKind.Other, $"SNMP device returned error '{pdu.ErrorStatus.ToErrorCode()}'.");

                ThrowIfV3Report(response);
            }

            foreach (var variable in pdu.Variables)
            {
                if (variable.Data.TypeCode == SnmpType.EndOfMibView)
                    breakLoop = true;

                if (!variable.Id.ToString().StartsWith(subTreeMask, StringComparison.Ordinal))
                    breakLoop = true;

                if (breakLoop)
                    break;

                results.Add(variable);
            }

            if (breakLoop)
                break;

            seed = pdu.Variables[^1];
            report = response;
        } while (!cancellationToken.IsCancellationRequested);

        return results.Select(v => new SnmpVariable(v.Id.ToString(), v.Data.ToString())).ToList();
    }

    private static void ThrowIfV3Report(ISnmpMessage response)
    {
        // Reuse the same known SNMPv3 error OIDs the upstream NETworkManager client uses.
        if (response is not ReportMessage)
            return;

        var pdu = response.Pdu();
        if (pdu.Variables.Count == 0)
            return;

        var oid = pdu.Variables[0].Id.ToString();
        if (oid == "1.3.6.1.6.3.15.1.1.3.0")
            throw new SnmpException(SnmpErrorKind.Authentication, "SNMPv3 username is unknown to the device.");
        if (oid == "1.3.6.1.6.3.15.1.1.5.0")
            throw new SnmpException(SnmpErrorKind.Authentication, "SNMPv3 authentication failed.");
    }

    private static IPrivacyProvider GetPrivacyProvider(SnmpCredential credential)
    {
        if (credential.Version != SnmpVersion.V3)
            return new DefaultPrivacyProvider(DefaultAuthenticationProvider.Instance);

        var auth = GetAuthenticationProvider(credential);

        return credential.SecurityLevel switch
        {
            SnmpSecurityLevel.AuthPriv => GetPrivacyProvider(credential.PrivAlgorithm, credential.PrivPassword, auth),
            SnmpSecurityLevel.AuthNoPriv => new DefaultPrivacyProvider(auth),
            _ => new DefaultPrivacyProvider(DefaultAuthenticationProvider.Instance),
        };
    }

    private static IAuthenticationProvider GetAuthenticationProvider(SnmpCredential credential)
    {
        var password = new OctetString(credential.AuthPassword ?? string.Empty);

        return credential.AuthAlgorithm switch
        {
#pragma warning disable CS0618 // MD5/SHA1 offered for compatibility with legacy devices.
            SnmpAuthAlgorithm.Md5 => new MD5AuthenticationProvider(password),
            SnmpAuthAlgorithm.Sha1 => new SHA1AuthenticationProvider(password),
#pragma warning restore CS0618
            SnmpAuthAlgorithm.Sha256 => new SHA256AuthenticationProvider(password),
            SnmpAuthAlgorithm.Sha384 => new SHA384AuthenticationProvider(password),
            SnmpAuthAlgorithm.Sha512 => new SHA512AuthenticationProvider(password),
            _ => DefaultAuthenticationProvider.Instance,
        };
    }

    private static IPrivacyProvider GetPrivacyProvider(SnmpPrivAlgorithm algorithm, string? privPassword,
        IAuthenticationProvider auth)
    {
        var password = new OctetString(privPassword ?? string.Empty);

        return algorithm switch
        {
#pragma warning disable CS0618 // DES offered for compatibility with legacy devices.
            SnmpPrivAlgorithm.Des => new DESPrivacyProvider(password, auth),
#pragma warning restore CS0618
            SnmpPrivAlgorithm.Aes => new AESPrivacyProvider(password, auth),
            SnmpPrivAlgorithm.Aes192 => new AES192PrivacyProvider(password, auth),
            SnmpPrivAlgorithm.Aes256 => new AES256PrivacyProvider(password, auth),
            _ => new DefaultPrivacyProvider(auth),
        };
    }
}
