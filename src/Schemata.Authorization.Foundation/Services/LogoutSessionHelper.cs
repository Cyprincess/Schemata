using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;

namespace Schemata.Authorization.Foundation.Services;

/// <summary>
///     Internal helper that reads the explicit OP-session participation facts recorded when an
///     OpenID Connect grant publishes a relying-party artifact. Both front-channel and
///     back-channel logout identify the relying parties to notify from these facts; credential
///     expiry, pruning, or revocation never loses a participant.
/// </summary>
internal static class LogoutSessionHelper
{
    public static async Task<IReadOnlyCollection<string>> GetSessionClientsAsync(
        ITokenStore<SchemataToken> tokens,
        string?                    subject,
        string?                    session,
        CancellationToken          ct
    ) {
        return await tokens.ListParticipantsAsync(subject, session, ct);
    }

}
