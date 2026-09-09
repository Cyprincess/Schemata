using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Services;

/// <summary>
///     Internal helper that discovers which client applications have active
///     tokens for a given subject or session.  Used by both front-channel and
///     back-channel logout services to identify RPs that need to be notified.
/// </summary>
internal static class LogoutSessionHelper
{
    public static readonly string[] LogoutParticipantTypes = [
        TokenTypes.AccessToken,
        TokenTypes.RefreshToken,
        TokenTypes.IdToken,
        TokenTypes.AuthorizationCode,
    ];

    public static async Task<HashSet<string>> GetSessionClientsAsync(
        ITokenStore<SchemataToken>   tokens,
        string?               subject,
        string?               session,
        CancellationToken     ct
    ) {
        return await GetSessionClientsAsync(tokens, subject, session, LogoutParticipantTypes, ct);
    }

    public static async Task<HashSet<string>> GetSessionClientsAsync(
        ITokenStore<SchemataToken>   tokens,
        string?               subject,
        string?               session,
        IReadOnlyCollection<string> types,
        CancellationToken     ct
    ) {
        var clients = new HashSet<string>();

        if (!string.IsNullOrWhiteSpace(session)) {
            await foreach (var token in tokens.ListBySessionAsync(session, ct)) {
                if (types.Count == 0 || (token.Type is { } t && types.Contains(t))) {
                    if (!string.IsNullOrWhiteSpace(token.Application)) {
                        clients.Add(token.Application);
                    }
                }
            }
        }

        // Only fall back to subject lookup when session data yielded no results.
        // Session-based lookup is more precise; subject-only lookup may span
        // multiple sessions.
        if (clients.Count != 0 || string.IsNullOrWhiteSpace(subject)) {
            return clients;
        }

        await foreach (var token in tokens.ListByParentAsync(subject, ct: ct)) {
            if (types.Count == 0 || (token.Type is { } t && types.Contains(t))) {
                if (!string.IsNullOrWhiteSpace(token.Application)) {
                    clients.Add(token.Application);
                }
            }
        }

        return clients;
    }
}