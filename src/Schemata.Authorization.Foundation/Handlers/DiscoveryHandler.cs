using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Advice;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Security.Skeleton;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Handlers;

/// <summary>
///     OIDC Discovery endpoint per
///     <seealso href="https://openid.net/specs/openid-connect-discovery-1_0.html#ProviderConfig">
///         OpenID Connect Discovery 1.0
///         §4: Obtaining OpenID Provider Configuration Information
///     </seealso>
///     .
///     Builds the OP's discovery document from <see cref="SchemataAuthorizationOptions" />
///     and the signing rows stored under the issuer; the JWKS itself is served by
///     <see cref="JwksHandler" />.
/// </summary>
public sealed class DiscoveryHandler<TScope>(
    IOptions<SchemataAuthorizationOptions> options,
    TokenService                          tokens,
    IScopeManager<TScope>                  scopes
)
    where TScope : SchemataScope
{
    /// <summary>
    ///     Returns the discovery document containing server metadata:
    ///     supported response types, response modes, grant types, subject types,
    ///     signing algorithms, claims, and the JWKS endpoint URI.
    ///     Runs the <see cref="IDiscoveryAdvisor" /> pipeline for extensibility.
    /// </summary>
    /// <param name="issuer">The issuer URI for this OP instance.</param>
    /// <param name="requireOpenIdMetadata">
    ///     Whether the OIDC metadata profile is required. OpenID Connect Discovery §3 requires
    ///     an advertised, usable RS256 signing algorithm. The OAuth metadata profile reports
    ///     the available algorithms without imposing that OIDC requirement.
    /// </param>
    /// <param name="ct">A cancellation token.</param>
    public async Task<AuthorizationResult> GetDiscoveryDocumentAsync(string issuer, bool requireOpenIdMetadata, CancellationToken ct) {
        var config = options.Value;

        var algorithms = await tokens.GetSigningAlgorithmsAsync(ct);
        if (requireOpenIdMetadata && !algorithms.Contains(SigningAlgorithms.RsaSha256)) {
            throw new OAuthException(OAuthErrors.ServerError, SchemataResources.INTERNAL);
        }


        var document = new DiscoveryDocument {
            Issuer                 = issuer,
            ResponseTypesSupported = config.AllowedResponseTypes.Count > 0 ? [..config.AllowedResponseTypes] : null,
            ResponseModesSupported = config.AllowedResponseModes.Count > 0 ? [..config.AllowedResponseModes] : null,
            SubjectTypesSupported = [SubjectTypes.Public],
            IdTokenSigningAlgValuesSupported           = algorithms.Count > 0 ? algorithms : null,
            ClaimsSupported                            = config.SupportedClaims.Count > 0 ? [..config.SupportedClaims] : null,
            AuthorizationResponseIssParameterSupported = !string.IsNullOrWhiteSpace(issuer),
        };

        var ctx = AdviceContext.Require();
        var discovery = new DiscoveryContext {
            Issuer                           = issuer,
            Document                         = document,
            SupportsAuthorizationResponseIss = !string.IsNullOrWhiteSpace(issuer),
        };

        switch (await Advisor.For<IDiscoveryAdvisor>()
                             .RunAsync(ctx, discovery, ct)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle:
                return AuthorizationResult.Content(discovery.Document);
            case AdviseResult.Block:
            default:
                throw new OAuthException(OAuthErrors.ServerError, SchemataResources.INTERNAL);
        }

        var names = await scopes.ListAsync(ct: ct)
            .Map(
                s => s.Name
                  ?? throw new OAuthException(OAuthErrors.ServerError, SchemataResources.INTERNAL),
                ct)
            .ToListAsync(ct);
        document.ScopesSupported = names.Count > 0 ? [..names] : null;

        return AuthorizationResult.Content(document);
    }

}
