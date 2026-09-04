using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Commands;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;

namespace Schemata.Authorization.Foundation.Advisors;

/// <summary>Order constants for <see cref="AdviceAuthorizeAuthorizationDetails{TApp}" />.</summary>
public static class AdviceAuthorizeAuthorizationDetails
{
    /// <summary>The default advisor ordering value.</summary>
    public const int DefaultOrder = AdviceAuthorizeClientAndRedirect.DefaultOrder + 1_000;
}

/// <summary>
///     Validates the <c>authorization_details</c> parameter at the authorization endpoint, per
/// <seealso href="https://www.rfc-editor.org/rfc/rfc9396.html#section-6">
///     RFC 9396: OAuth 2.0 Rich Authorization
///     Requests §6: Authorization Request Processing
/// </seealso>
///     , and publishes the accepted grant set on the ambient advice context.
/// </summary>
/// <remarks>
///     Registered only by <see cref="Features.RichAuthorizationFeature{TApp}" />. Without the
///     feature the parameter is ignored — RFC 6749 §3.1 requires the authorization server to
///     ignore unrecognized request parameters — so it binds, reaches no grant, and never reaches
///     the interaction payload.
/// </remarks>
public sealed class AdviceAuthorizeAuthorizationDetails<TApp>(
    AuthorizationDetailsService details
) : IAuthorizeAdvisor<TApp>
    where TApp : SchemataApplication
{
    #region IAuthorizeAdvisor<TApp> Members

    public int Order => AdviceAuthorizeAuthorizationDetails.DefaultOrder;

    public async Task<AdviseResult> AdviseAsync(AdviceContext ctx, AuthorizeContext<TApp> authz, CancellationToken ct = default) {
        var raw = authz.Request?.AuthorizationDetails;
        if (string.IsNullOrWhiteSpace(raw)) {
            return AdviseResult.Continue;
        }

        var parsed = details.Parse(raw, ct);

        // §10 client metadata: every requested type must be among the client's registered types.
        var registered = authz.Application?.AuthorizationDetailsTypes;
        if (registered is { Count: > 0 }) {
            var requested = new HashSet<string>();
            foreach (var node in parsed) {
                if (node?["type"]?.GetValue<string>() is { } type) {
                    requested.Add(type);
                }
            }
            if (requested.Any(t => !registered.Contains(t))) {
                throw new OAuthException(
                    OAuthErrors.InvalidRequest,
                    SchemataResources.GetResourceString(SchemataResources.INVALID_AUTHORIZATION_DETAILS_TYPE_UNSUPPORTED)
                );
            }
        }

        if (parsed.Count > 0) {
            ctx.Set(new AuthorizationDetailsGrant(parsed.ToJsonString()));
        }

        return AdviseResult.Continue;
    }

    #endregion
}
