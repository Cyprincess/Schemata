using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Security.Skeleton.Entities;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Advisors;

/// <summary>Order constants for <see cref="AdviceIntrospectionDpop{TApp}" />.</summary>
public static class AdviceIntrospectionDpop
{
    /// <summary>The default advisor ordering value.</summary>
    public const int DefaultOrder = AdviceIntrospectionAuthorizationDetails.DefaultOrder + 10_000_000;
}

/// <summary>
///     Echoes the token's confirmation (<c>cnf</c>) claim onto the introspection response and derives
///     <c>token_type</c> from the proof-of-possession binding, per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc9449.html#section-6.2">
///         RFC 9449: OAuth 2.0 Demonstrating Proof-of-Possession at the Application Layer (DPoP)
///         §6.2: Introspection Response
///     </seealso>
///     : <c>cnf</c> echoes as a top-level response member, and a token bound via <c>cnf.jkt</c>
///     reports <c>token_type: DPoP</c>. Tokens without a usable confirmation claim keep the
///     handler's <c>Bearer</c> default and echo nothing.
/// </summary>
/// <typeparam name="TApp">The application entity type.</typeparam>
/// <remarks>
///     Registered only by the DPoP flow feature; a host that does not install it introspects
///     Bearer tokens only.
/// </remarks>
public sealed class AdviceIntrospectionDpop<TApp> : IIntrospectionAdvisor<TApp>
    where TApp : SchemataApplication
{
    #region IIntrospectionAdvisor<TApp> Members

    public int Order => AdviceIntrospectionDpop.DefaultOrder;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext                      ctx,
        IntrospectionContext<TApp> introspection,
        CancellationToken                  ct = default
    ) {
        var cnf = GetCnf(introspection.Principal);
        if (cnf is null) {
            return Task.FromResult(AdviseResult.Continue);
        }

        introspection.Response!.Cnf      = cnf;
        introspection.Response.TokenType = cnf.ContainsKey(Claims.Jkt) ? Schemes.Dpop : Schemes.Bearer;

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion

    private static Dictionary<string, string>? GetCnf(ClaimsPrincipal? principal) {
        var json = principal?.FindFirstValue(Claims.Cnf);
        if (string.IsNullOrWhiteSpace(json)) {
            return null;
        }

        JsonDocument document;
        try {
            document = JsonDocument.Parse(json);
        } catch (JsonException) {
            // A cnf claim that is not a JSON object carries no echoable confirmation data.
            return null;
        }

        if (document.RootElement.ValueKind != JsonValueKind.Object) {
            document.Dispose();
            return null;
        }

        using (document) {
            var cnf = new Dictionary<string, string>();
            foreach (var property in document.RootElement.EnumerateObject()) {
                if (property.Value.ValueKind == JsonValueKind.String) {
                    cnf[property.Name] = property.Value.GetString()!;
                }
            }

            return cnf.Count > 0 ? cnf : null;
        }
    }
}
