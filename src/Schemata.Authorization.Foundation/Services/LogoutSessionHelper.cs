using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;

namespace Schemata.Authorization.Foundation.Services;

/// <summary>
///     Internal helper that discovers which client applications have active
///     tokens for a given subject or session.  Used by both front-channel and
///     back-channel logout services to identify RPs that need to be notified.
/// </summary>
internal static class LogoutSessionHelper
{
    /// <summary>
    ///     Collects unique application names from all non-expired tokens
    ///     associated with the given session or subject.  Session lookup is
    ///     preferred when available (more targeted); falls back to subject
    ///     lookup when no session tokens are found.
    /// </summary>
    public static async Task<HashSet<string>> GetSessionClientsAsync<TToken>(
        ITokenManager<TToken> tokens,
        string?               subject,
        string?               session,
        CancellationToken     ct
    )
        where TToken : SchemataToken {
        var clients = new HashSet<string>();

        if (!string.IsNullOrWhiteSpace(session)) {
            await foreach (var token in tokens.ListBySessionAsync(session, ct)) {
                if (!string.IsNullOrWhiteSpace(token.ApplicationName)) {
                    clients.Add(token.ApplicationName);
                }
            }
        }

        // Only fall back to subject lookup when session data yielded no results.
        // Session-based lookup is more precise; subject-only lookup may span
        // multiple sessions.
        if (clients.Count != 0 || string.IsNullOrWhiteSpace(subject)) {
            return clients;
        }

        await foreach (var token in tokens.ListBySubjectAsync(subject, ct)) {
            if (!string.IsNullOrWhiteSpace(token.ApplicationName)) {
                clients.Add(token.ApplicationName);
            }
        }

        return clients;
    }
}
