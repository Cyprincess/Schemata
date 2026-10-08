using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Security.Skeleton.Entities;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Advisors;

/// <summary>Order constants for <see cref="AdviceIntrospectionAuthorizationDetails{TApp}" />.</summary>
public static class AdviceIntrospectionAuthorizationDetails
{
    /// <summary>The default advisor ordering value.</summary>
    public const int DefaultOrder = AdviceIntrospectionTokenValidation.DefaultOrder + 10_000_000;
}

/// <summary>
///     Echoes the token's <c>authorization_details</c> claim as a top-level introspection response
///     member, filtered for the resource server making the request, per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc9396.html#section-9.2">
///         RFC 9396: OAuth 2.0 Rich Authorization Requests
///         §9.2: Introspection Response
///     </seealso>
///     . This server discloses location-bound details only to the authenticated caller's
///     configured resource audiences. Details without locations retain their grant-wide meaning.
/// </summary>
/// <typeparam name="TApp">The application entity type.</typeparam>
/// <remarks>
///     Registered only by the rich authorization requests flow feature; tokens minted without it
///     carry no <c>authorization_details</c> claim, so there is nothing to echo.
/// </remarks>
public sealed class AdviceIntrospectionAuthorizationDetails<TApp>(IOptions<SchemataAuthorizationOptions> options) : IIntrospectionAdvisor<TApp>
    where TApp : SchemataApplication
{
    #region IIntrospectionAdvisor<TApp> Members

    public int Order => AdviceIntrospectionAuthorizationDetails.DefaultOrder;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext                      ctx,
        IntrospectionContext<TApp> introspection,
        CancellationToken                  ct = default
    ) {
        var response = introspection.Response;
        if (response is null) {
            return Task.FromResult(AdviseResult.Block);
        }

        var principal = introspection.Principal;
        if (principal is null) {
            return Task.FromResult(AdviseResult.Continue);
        }

        var clientId = introspection.Application?.ClientId;
        IReadOnlyList<string> audiences = clientId is not null
            && options.Value.IntrospectionResourceAudiences.TryGetValue(clientId, out var allowed)
                ? principal.FindAll(Claims.Audience).Select(claim => claim.Value).Where(allowed.Contains).ToArray() : [];

        var details = GetAuthorizationDetails(principal, audiences);
        if (details is not null) {
            response.AuthorizationDetails = details;
        }

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion

    private static JsonElement? GetAuthorizationDetails(ClaimsPrincipal principal, IReadOnlyList<string> audiences) {
        var details = new List<JsonElement>();
        foreach (var claim in principal.FindAll(Claims.AuthorizationDetails)) {
            if (string.IsNullOrWhiteSpace(claim.Value)) {
                continue;
            }

            JsonDocument document;
            try {
                document = JsonDocument.Parse(claim.Value);
            } catch (JsonException) {
                continue;
            }

            using (document) {
                if (document.RootElement.ValueKind == JsonValueKind.Array) {
                    foreach (var element in document.RootElement.EnumerateArray()) {
                        AddRelevantDetail(details, element, audiences);
                    }
                } else if (document.RootElement.ValueKind == JsonValueKind.Object) {
                    AddRelevantDetail(details, document.RootElement, audiences);
                }
            }
        }

        if (details.Count == 0) {
            return null;
        }

        using var combined = JsonDocument.Parse($"[{string.Join(",", details.Select(static d => d.GetRawText()))}]");
        return combined.RootElement.Clone();
    }

    private static void AddRelevantDetail(List<JsonElement> details, JsonElement element, IReadOnlyList<string> audiences) {
        if (element.ValueKind != JsonValueKind.Object) {
            return;
        }

        if (element.TryGetProperty("locations", out var locations)) {
            var relevant = locations.ValueKind == JsonValueKind.Array
                        && locations.EnumerateArray().Any(
                               l => l.ValueKind == JsonValueKind.String && audiences.Contains(l.GetString()!));
            if (!relevant) {
                return;
            }
        }

        details.Add(element.Clone());
    }
}
