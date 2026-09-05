using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
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
///     . The strict reading of §9.2 ("potentially filtered and extended for the RS making the
///     introspection request") follows §9.1, which scopes the same data "to the specific audience":
///     a detail carrying <c>locations</c> (§2.2 common data fields — the RS endpoints a detail
///     applies to) echoes only when a location equals one of the token's <c>aud</c> claims, while
///     a detail without <c>locations</c> declares no RS boundary and passes unfiltered. A
///     <c>locations</c> member that is not a string array declares no matching RS and drops the
///     detail. JWT validation splits a JSON-array claim into one claim per element, so both the
///     whole-array and per-element claim shapes are assembled here.
/// </summary>
/// <typeparam name="TApp">The application entity type.</typeparam>
/// <remarks>
///     Registered only by the rich authorization requests flow feature; tokens minted without it
///     carry no <c>authorization_details</c> claim, so there is nothing to echo.
/// </remarks>
public sealed class AdviceIntrospectionAuthorizationDetails<TApp> : IIntrospectionAdvisor<TApp>
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

        // RFC 9068 §3: a token minted for several resource indicators carries one aud claim per
        // value, so the relevance test runs against all of them.
        var audiences = principal.FindAll(Claims.Audience).Select(c => c.Value).ToList();

        var details = GetAuthorizationDetails(principal, audiences);
        if (details is not null) {
            response.AuthorizationDetails = details;
        }

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion

    private static string? GetAuthorizationDetails(ClaimsPrincipal principal, IReadOnlyList<string> audiences) {
        var details = new JsonArray();
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

        return details.Count > 0 ? details.ToJsonString() : null;
    }

    private static void AddRelevantDetail(JsonArray details, JsonElement element, IReadOnlyList<string> audiences) {
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

        details.Add(JsonNode.Parse(element.GetRawText()));
    }
}
