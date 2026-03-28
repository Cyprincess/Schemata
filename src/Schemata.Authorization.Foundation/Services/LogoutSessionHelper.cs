using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;

namespace Schemata.Authorization.Foundation.Services;

internal static class LogoutSessionHelper
{
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
