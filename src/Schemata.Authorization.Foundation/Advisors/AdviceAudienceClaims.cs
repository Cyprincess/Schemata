using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Skeleton.Advisors;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Advisors;

/// <summary>
///     Claims advisor that derives the <c>aud</c> claim from the authorized client_id when not explicitly set,
///     per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-4.1.3">
///         RFC 6749: The OAuth 2.0 Authorization
///         Framework §4.1.3: Access Token Request
///     </seealso>
///     .
/// </summary>
/// <remarks>
///     When no audience is present, the authorised client_id is reused as the audience,
///     ensuring the access token is intended for that application.
/// </remarks>
/// <seealso cref="IClaimsAdvisor" />
public sealed class AdviceAudienceClaims : IClaimsAdvisor
{
    public const int DefaultOrder = Orders.Base;

    #region IClaimsAdvisor Members

    /// <inheritdoc cref="AdviseResult" />
    public int Order => DefaultOrder;

    /// <inheritdoc />
    public Task<AdviseResult> AdviseAsync(AdviceContext ctx, List<Claim> claims, CancellationToken ct = default) {
        if (claims.Any(c => c.Type == Claims.Audience)) {
            return Task.FromResult(AdviseResult.Continue);
        }

        var client = claims.FirstOrDefault(c => c.Type == Claims.ClientId)?.Value;
        if (!string.IsNullOrWhiteSpace(client)) {
            claims.Add(new(Claims.Audience, client));
        }

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
