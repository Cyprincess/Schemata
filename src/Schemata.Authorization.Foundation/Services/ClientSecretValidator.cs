using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Security.Skeleton;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Services;

/// <summary>
///     Shared client-secret validation for the client authentication methods per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-2.3.1">
///         RFC 6749: The OAuth 2.0 Authorization
///         Framework §2.3.1: Client Password
///     </seealso>
///     .
///     A confidential client's secret lives in its newest valid password security row;
///     confidential clients MUST present it, public clients may authenticate without one.
/// </summary>
internal static class ClientSecretValidator
{
    public static async Task ValidateAsync<TSecurity>(
        ISecurityStore<TSecurity> securities,
        ISecretVerifier          verifier,
        SchemataApplication      app,
        string?                  secret,
        CancellationToken        ct
    ) where TSecurity : SchemataSecurity {
        if (string.IsNullOrWhiteSpace(secret)) {
            if (app.ClientType == ClientTypes.Confidential) {
                throw new OAuthException(
                    OAuthErrors.InvalidClient,
                    SchemataResources.GetResourceString(SchemataResources.CLIENT_SECRET_REQUIRED)
                );
            }

            return;
        }

        // The store orders rows newest-first; only the newest valid row decides —
        // an older row is never consulted (rotation semantics).
        TSecurity? newest = null;
        await foreach (var row in securities.ListByParentAsync(
                           SecurityParents.Application(app),
                           SecurityConstants.Kinds.Password,
                           SecurityConstants.Usages.Authentication,
                           SecurityConstants.Statuses.Valid,
                           ct)) {
            newest = row;
            break;
        }

        if (newest is null || !await verifier.VerifyAsync(newest, secret, ct)) {
            throw new OAuthException(
                OAuthErrors.InvalidClient,
                SchemataResources.GetResourceString(SchemataResources.INVALID_CLIENT_CREDENTIALS)
            );
        }
    }
}
