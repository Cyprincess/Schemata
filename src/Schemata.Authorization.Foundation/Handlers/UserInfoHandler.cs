using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Advice;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Handlers;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Handlers;

/// <summary>
///     OIDC UserInfo endpoint per
///     <seealso href="https://openid.net/specs/openid-connect-core-1_0.html#UserInfo">
///         OpenID Connect Core 1.0 §5.3:
///         UserInfo Endpoint
///     </seealso>
///     .
///     Returns claims about the authenticated end-user.  Runs the
///     <see cref="IUserInfoAdvisor" /> pipeline to fill the user-info context,
///     then the <see cref="IClaimsAdvisor" /> pipeline to resolve claims,
///     and finally filters claims by <see cref="IDestinationAdvisor" /> to
///     only include those allowed for the <c>userinfo</c> destination.
/// </summary>
public sealed class UserInfoHandler(IServiceProvider sp) : UserInfoEndpoint
{
    /// <summary>
    ///     Returns user claims scoped to the userinfo destination.
    ///     Filters by OIDC scope, runs advisor pipelines for claim resolution
    ///     and destination filtering, and returns claims as a JSON object.
    ///     Multiple values for the same claim type are returned as an array.
    /// </summary>
    /// <param name="principal">The authenticated principal extracted from the access token.</param>
    /// <param name="ct">Cancellation token.</param>
    public override async Task<AuthorizationResult> HandleAsync(ClaimsPrincipal principal, CancellationToken ct) {
        var ctx = new AdviceContext(sp);

        var sub    = principal.FindFirstValue(Claims.Subject);
        var scope  = principal.FindFirstValue(Claims.Scope);
        var client = principal.FindFirstValue(Claims.ClientId);
        var scopes = ScopeParser.Parse(scope);

        var info = new UserInfoContext {
            Principal       = principal,
            InternalSubject = sub,
            GrantedScopes   = scopes,
            IsEndUserToken  = !string.IsNullOrWhiteSpace(sub),
        };

        switch (await Advisor.For<IUserInfoAdvisor>()
                             .RunAsync(ctx, info, ct)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<AuthorizationResult>(out var result):
                return result!;
            case AdviseResult.Block:
            default:
                throw new OAuthException(
                    OAuthErrors.AccessDenied,
                    SchemataResources.GetResourceString(SchemataResources.ST4008)
                );
        }

        var claims = new List<Claim>();

        if (!string.IsNullOrWhiteSpace(sub)) {
            claims.Add(new(Claims.Subject, sub));
        }

        if (!string.IsNullOrWhiteSpace(client)) {
            claims.Add(new(Claims.ClientId, client));
        }

        switch (await Advisor.For<IClaimsAdvisor>()
                             .RunAsync(ctx, claims, ct)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<AuthorizationResult>(out var result):
                return result!;
            case AdviseResult.Block:
            default:
                throw new OAuthException(
                    OAuthErrors.AccessDenied,
                    SchemataResources.GetResourceString(SchemataResources.ST4008)
                );
        }

        
        foreach (var claim in claims) {
            var destinations = new HashSet<string>();

            switch (await Advisor.For<IDestinationAdvisor>()
                                 .RunAsync(ctx, claim, destinations, principal, ct)) {
                case AdviseResult.Continue:
                case AdviseResult.Handle:
                    break;
                case AdviseResult.Block:
                default:
                    continue;
            }

            if (destinations.Count == 0) {
                continue;
            }

            foreach (var d in destinations) {
                claim.Properties[d] = Parameters.Token;
            }
        }

        var list = claims.Where(c => c.Properties.ContainsKey(ClaimDestinations.UserInfo)).ToList();

        // Flatten duplicate claim types: single value → string, multiple → array.
        var dict = list.GroupBy(c => c.Type)
                       .ToDictionary(
                            g => g.Key,
                            g => g.Count() == 1 ? (object)g.First().Value : g.Select(c => c.Value).ToArray());

        return AuthorizationResult.Content(dict);
    }
}
