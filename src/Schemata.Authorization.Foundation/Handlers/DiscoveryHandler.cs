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
    ISecurityStore<SchemataSecurity>       securities,
    IScopeManager<TScope>                  scopes
)
    where TScope : SchemataScope
{
    /// <summary>
    ///     Returns the OIDC discovery document containing server metadata:
    ///     supported response types, response modes, grant types, subject types,
    ///     signing algorithms, claims, and the JWKS endpoint URI.
    ///     Runs the <see cref="IDiscoveryAdvisor" /> pipeline for extensibility.
    /// </summary>
    /// <param name="issuer">The issuer URI for this OP instance.</param>
    /// <param name="ct">A cancellation token.</param>
    public async Task<AuthorizationResult> GetDiscoveryDocumentAsync(string issuer, CancellationToken ct) {
        var config = options.Value;

        var signingRows = new List<SchemataSecurity>();
        await foreach (var row in securities.ListByParentAsync(
                           SecurityParents.Issuer(issuer), null, SecurityConstants.Usages.Signing, null, ct)) {
            signingRows.Add(row);
        }

        var algorithm = SecurityKeyAdapter.ToSigningAlgorithm(
            signingRows.FirstOrDefault(
                row => row.Status == SecurityConstants.Statuses.Valid)?.Algorithm);

        var document = new DiscoveryDocument {
            Issuer                 = issuer,
            ResponseTypesSupported = [..config.AllowedResponseTypes],
            ResponseModesSupported = [..config.AllowedResponseModes],
            SubjectTypesSupported = [SubjectTypes.Public],
            IdTokenSigningAlgValuesSupported           = algorithm is null ? null : [algorithm],
            ClaimsSupported                            = [..config.SupportedClaims],
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
                throw new OAuthException(
                    OAuthErrors.ServerError,
                    SchemataResources.GetResourceString(SchemataResources.INTERNAL)
                );
        }

        var names = await scopes.ListAsync(ct: ct)
            .Map(
                s => s.Name
                  ?? throw new OAuthException(
                      OAuthErrors.ServerError,
                      SchemataResources.GetResourceString(SchemataResources.INTERNAL)),
                ct)
            .ToListAsync(ct);
        document.ScopesSupported = [..names];

        return AuthorizationResult.Content(document);
    }

}
