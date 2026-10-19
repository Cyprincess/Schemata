using System.Collections.Generic;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Entities;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Core.Building;
using Schemata.Security.Skeleton.Entities;
using Schemata.Transport.Http.Linking;

namespace Schemata.Authorization.Http.Linking;

/// <summary>
///     Contributes the authorization management relations: an application reaches its token and scope
///     collections, a token reaches its application and persisted parent, and a scope reaches the
///     applications granted it. Targets contain resource names only; token values and references
///     never enter a link.
/// </summary>
public sealed class AuthorizationLinkContributor(ResourceRegistry registry) : ILinkContributor
{
    #region ILinkContributor Members

    public IEnumerable<WebLink> GetLinks(LinkContext context) {
        if (context.Resource is not { } resource) {
            yield break;
        }

        var request = context.HttpContext.Request;

        if (typeof(SchemataApplication).IsAssignableFrom(resource.Entity)) {
            foreach (var link in Application(request, context.Value as ICanonicalName)) {
                yield return link;
            }
        } else if (typeof(SchemataToken).IsAssignableFrom(resource.Entity)) {
            foreach (var link in Token(request, context.Value as SchemataToken)) {
                yield return link;
            }
        } else if (typeof(SchemataScope).IsAssignableFrom(resource.Entity)) {
            foreach (var link in Scope(request, context.Value as ICanonicalName)) {
                yield return link;
            }
        }
    }

    #endregion

    private IEnumerable<WebLink> Application(HttpRequest request, ICanonicalName? value) {
        if (value?.CanonicalName is not { Length: > 0 } canonical) {
            yield break;
        }

        if (ResourceLinkTargets.FindCollection(registry, typeof(SchemataToken)) is { } tokens) {
            yield return new(ResourceLinkTargets.Collection(request, tokens,
                                                            "application=" + JsonSerializer.Serialize(canonical)),
                             "related", Title: "tokens");
        }

        if (ResourceLinkTargets.FindCollection(registry, typeof(SchemataScope)) is { } scopes) {
            yield return new(ResourceLinkTargets.Collection(request, scopes), "related", Title: "scopes");
        }
    }

    private IEnumerable<WebLink> Token(HttpRequest request, SchemataToken? token) {
        if (token is null) {
            yield break;
        }

        if (Canonical(request, token.Application) is { } application) {
            yield return new(application, "related", Title: "application");
        }

        if (Canonical(request, token.Parent) is { } parent) {
            yield return new(parent, "related", Title: "parent");
        }
    }

    private IEnumerable<WebLink> Scope(HttpRequest request, ICanonicalName? value) {
        var name = value?.Name ?? Leaf(value?.CanonicalName);
        if (string.IsNullOrWhiteSpace(name)) {
            yield break;
        }

        if (ResourceLinkTargets.FindCollection(registry, typeof(SchemataApplication)) is { } applications) {
            yield return new(ResourceLinkTargets.Collection(request, applications,
                                                            "scope:" + JsonSerializer.Serialize(name)),
                             "related", Title: "applications");
        }
    }

    private string? Canonical(HttpRequest request, string? canonicalName) {
        if (string.IsNullOrWhiteSpace(canonicalName)) {
            return null;
        }

        var registration = ResourceLinkTargets.Match(registry, canonicalName);

        return registration is null ? null : ResourceLinkTargets.Canonical(request, registration, canonicalName);
    }

    private static string? Leaf(string? canonicalName) => canonicalName?.Split('/') is { Length: > 0 } segments ? segments[^1] : null;
}
