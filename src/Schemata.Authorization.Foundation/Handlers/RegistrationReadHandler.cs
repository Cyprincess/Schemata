using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Authorization.Foundation.Queries;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using Schemata.Messaging.Skeleton;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Handlers;

/// <summary>
///     Reads back a dynamically registered client's metadata with a registration access token, per
///     <seealso href="https://openid.net/specs/openid-connect-registration-1_0.html">
///         OpenID Connect Dynamic Client Registration 1.0 §3.3: Client Read Request
///     </seealso>
///     .
/// </summary>
internal sealed class RegistrationReadHandler<TApp>(
    IApplicationManager<TApp>        apps,
    ITokenStore<SchemataToken>            tokens,
    ISecurityStore<SchemataSecurity> securities,
    TimeProvider?                    time = null
) : IRequestHandler<RegisterReadQuery, RegistrationResponse?>
    where TApp : SchemataApplication
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    #region IRequestHandler<RegisterReadQuery, RegistrationResponse?> Members

    public async Task<RegistrationResponse?> HandleAsync(RegisterReadQuery query, CancellationToken ct = default) {
        var (clientId, bearerToken) = query;
        if (string.IsNullOrWhiteSpace(bearerToken) || string.IsNullOrWhiteSpace(clientId)) {
            return null;
        }

        var token = await tokens.FindByReferenceIdAsync(bearerToken, ct);
        if (token?.Type != TokenTypes.Registration
            || token.Status != TokenStatuses.Valid
            || token.ExpireTime is { } expiry && expiry <= _time.GetUtcNow().UtcDateTime) {
            return null;
        }

        if (string.IsNullOrWhiteSpace(token.Payload)) {
            return null;
        }

        string bound;
        try {
            bound = JsonSerializer.Deserialize<RegistrationTokenPayload>(token.Payload)?.ClientId ?? string.Empty;
        } catch (JsonException) {
            return null;
        }

        if (!string.Equals(bound, clientId, StringComparison.Ordinal)) {
            return null;
        }

        var application = await apps.FindByClientIdAsync(clientId, ct);
        return application is null ? null : await RegistrationMetadataMapper.ToResponse(application, securities, ct);
    }

    #endregion

}
