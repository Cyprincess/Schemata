using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Foundation.Advisors;

public static class AdviceClaimsAudience
{
    public const int DefaultOrder = Orders.Base;
}

/// <summary>
///     Claims advisor that derives the <c>aud</c> claim from the authorized application's canonical name when the
///     request omits it,
///     per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-4.1.3">
///         RFC 6749: The OAuth 2.0 Authorization
///         Framework §4.1.3: Access Token Request
///     </seealso>
///     .
/// </summary>
/// <remarks>
///     When no audience is present, the authorised application canonical name is used as the audience,
///     matching the application reference persisted on issued access tokens.
/// </remarks>
/// <seealso cref="IClaimsAdvisor" />
public sealed class AdviceClaimsAudience<TApp>(IApplicationManager<TApp> applications) : IClaimsAdvisor
    where TApp : SchemataApplication
{
    #region IClaimsAdvisor Members

    public int Order => AdviceClaimsAudience.DefaultOrder;

    public async Task<AdviseResult> AdviseAsync(AdviceContext ctx, List<Claim> claims, CancellationToken ct = default) {
        if (claims.Any(c => c.Type == Claims.Audience)) {
            return AdviseResult.Continue;
        }

        var client = claims.FirstOrDefault(c => c.Type == Claims.ClientId)?.Value;
        if (string.IsNullOrWhiteSpace(client)) {
            return AdviseResult.Continue;
        }

        var application = await applications.FindByClientIdAsync(client, ct);
        if (!string.IsNullOrWhiteSpace(application?.CanonicalName)) {
            claims.Add(new(Claims.Audience, application.CanonicalName));
        }

        return AdviseResult.Continue;
    }

    #endregion
}
