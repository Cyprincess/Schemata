using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Models;
using static Schemata.Abstractions.SchemataConstants;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Advisors;

/// <summary>
///     Claims advisor that mints the <c>aud</c> claims for both token destinations,
///     per
///     <seealso href="https://openid.net/specs/openid-connect-core-1_0.html#IDToken">
///         OpenID Connect Core 1.0 §2: ID Token
///     </seealso>
///     ,
///     <seealso href="https://www.rfc-editor.org/rfc/rfc9068.html#section-2.2">
///         RFC 9068: JSON Web Token (JWT) Profile
///         for OAuth 2.0 Access Tokens §2.2: Data Structure
///     </seealso>
///     and
///     <seealso href="https://www.rfc-editor.org/rfc/rfc8707.html#section-2">
///         RFC 8707: Resource Indicators for OAuth 2.0 §2: Resource Parameter
///     </seealso>
///     .
/// </summary>
/// <remarks>
///     An explicit <c>aud</c> claim is preserved untouched. Otherwise the access token audience
///     follows the request: the adopted resource indicators mint one <c>aud</c> claim per value
///     verbatim, with no default resource or issuer mixed in, and without them the claim falls
///     back to <c>DefaultResource ?? Issuer</c>. Each access token audience is pre-tagged with a
///     single destination so the downstream destination split routes them without further
///     handling. The ID token audience is a separate, client-owned fact: <c>aud = client_id</c>
///     is minted whenever no audience already reaching the ID-token destination carries that
///     value (OIDC Core §2), even beside unrelated explicit access audiences.
/// </remarks>
/// <seealso cref="IClaimsAdvisor" />
/// <seealso cref="AdviceDestinationSubject" />
public sealed class AdviceClaimsAudience(IOptions<SchemataAuthorizationOptions> options) : IClaimsAdvisor
{
    /// <summary>The default advisor ordering value.</summary>
    public const int DefaultOrder = Orders.Base;
    #region IClaimsAdvisor Members

    public int Order => DefaultOrder;

    public Task<AdviseResult> AdviseAsync(AdviceContext ctx, List<Claim> claims, Schemata.Authorization.Skeleton.Models.AuthorizationClaimContext issuance, CancellationToken ct = default) {
        if (!claims.Any(c => c.Type == Claims.Audience)) {
            var indicators = issuance.AccessResources ?? claims.FirstOrDefault(c => c.Type == Claims.Resources)?.Value.Split(
                ' ', StringSplitOptions.RemoveEmptyEntries);
            var config = options.Value;
            if (indicators is { Count: > 0 }) {
                foreach (var resource in indicators) {
                    var access = new Claim(Claims.Audience, resource) { Properties = {
                            [ClaimDestinations.AccessToken] = Parameters.Token,
                        },
                    };
                    claims.Add(access);
                }
            } else {
                var audience = string.IsNullOrWhiteSpace(config.DefaultResource) ? config.Issuer : config.DefaultResource;
                if (!string.IsNullOrWhiteSpace(audience)) {
                    var access = new Claim(Claims.Audience, audience) { Properties = {
                            [ClaimDestinations.AccessToken] = Parameters.Token,
                        },
                    };
                    claims.Add(access);
                }
            }
        }

        var client = claims.FirstOrDefault(c => c.Type == Claims.ClientId)?.Value;
        if (!string.IsNullOrWhiteSpace(client)) {
            // AdviceDestinationSubject routes an untagged aud to both destinations, so an
            // audience reaches the ID token when it is untagged or carries the ID-tagged
            // property. An explicit access-only audience never satisfies the ID client_id.
            var coversIdentity = claims.Any(c => c.Type == Claims.Audience
                                              && (c.Properties.Count == 0 || c.Properties.ContainsKey(ClaimDestinations.IdentityToken))
                                              && string.Equals(c.Value, client, StringComparison.Ordinal));
            if (!coversIdentity) {
                var identity = new Claim(Claims.Audience, client) { Properties = { [ClaimDestinations.IdentityToken] = Parameters.Token },
                };
                claims.Add(identity);
            }
        }

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
