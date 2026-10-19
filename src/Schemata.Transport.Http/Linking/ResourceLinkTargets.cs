using System;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Core.Building;

namespace Schemata.Transport.Http.Linking;

/// <summary>
///     Resolves the HTTP resource surface and builds link targets from persisted resource names.
///     A target exists only when the registration is published over HTTP and, for a collection, when
///     the registration permits <see cref="Operations.List" />.
/// </summary>
public static class ResourceLinkTargets
{
    /// <summary>
    ///     Finds the HTTP registration of <paramref name="entity" />.
    /// </summary>
    /// <param name="registry">The configured resources.</param>
    /// <param name="entity">The entity type, matched against derived registrations.</param>
    /// <returns>The registration, or <see langword="null" /> when the entity is not published over HTTP.</returns>
    public static ResourceRegistration? Find(ResourceRegistry registry, Type entity) =>
        registry.Resources.FirstOrDefault(resource => entity.IsAssignableFrom(resource.Entity) && IsPublished(resource));

    /// <summary>
    ///     Finds the HTTP registration of <paramref name="entity" /> when it permits listing.
    /// </summary>
    /// <param name="registry">The configured resources.</param>
    /// <param name="entity">The entity type, matched against derived registrations.</param>
    /// <returns>The registration, or <see langword="null" /> when the collection is not addressable.</returns>
    public static ResourceRegistration? FindCollection(ResourceRegistry registry, Type entity) =>
        Find(registry, entity) is { } resource && AllowsList(resource) ? resource : null;

    /// <summary>
    ///     Finds the HTTP registration whose canonical pattern owns <paramref name="canonicalName" />.
    /// </summary>
    /// <param name="registry">The configured resources.</param>
    /// <param name="canonicalName">The persisted canonical resource name.</param>
    /// <returns>The registration, or <see langword="null" /> when no published pattern matches.</returns>
    public static ResourceRegistration? Match(ResourceRegistry registry, string canonicalName) {
        var segments = canonicalName.Split('/');
        foreach (var resource in registry.Resources) {
            if (!IsPublished(resource)) {
                continue;
            }

            var pattern = ResourceNameDescriptor.ForType(resource.Entity).Pattern;
            if (pattern is null) {
                continue;
            }

            var parts = pattern.Split('/');
            if (parts.Length != segments.Length) {
                continue;
            }

            var matches = true;
            for (var index = 0; index < parts.Length; index++) {
                if (ResourceNameDescriptor.IsPlaceholder(parts[index])) {
                    continue;
                }

                if (!string.Equals(parts[index], segments[index], StringComparison.OrdinalIgnoreCase)) {
                    matches = false;
                    break;
                }
            }

            if (matches) {
                return resource;
            }
        }

        return null;
    }

    /// <summary>
    ///     Builds the versioned route prefix of a registration, honoring its resource package.
    /// </summary>
    /// <param name="registration">The resolved registration.</param>
    /// <returns>The encoded-path prefix ending in <c>/</c>.</returns>
    public static string Prefix(ResourceRegistration registration) =>
        ResourceNameDescriptor.ForType(registration.Entity).Package is { } package
            ? $"/v1/{package.ToLowerInvariant()}/"
            : "/v1/";

    /// <summary>
    ///     Builds the target of a persisted canonical resource name.
    /// </summary>
    /// <param name="request">The current request.</param>
    /// <param name="registration">The registration owning the name.</param>
    /// <param name="canonicalName">The persisted canonical resource name or resource-relative path.</param>
    /// <returns>The relative target.</returns>
    public static string Canonical(HttpRequest request, ResourceRegistration registration, string canonicalName) =>
        UriHelper.BuildRelative(request.PathBase,
                                new PathString(LinkTargets.EncodePath(Prefix(registration) + canonicalName)));

    /// <summary>
    ///     Builds the target of one addressable resource by its leaf name.
    /// </summary>
    /// <param name="request">The current request.</param>
    /// <param name="registration">The registration owning the resource.</param>
    /// <param name="name">The leaf resource name.</param>
    /// <returns>The relative target, or <see langword="null" /> when the registration is not addressable.</returns>
    public static string? Instance(HttpRequest request, ResourceRegistration registration, string name) {
        var descriptor = ResourceNameDescriptor.ForType(registration.Entity);
        if (!descriptor.IsAddressable) {
            return null;
        }

        return Canonical(request, registration, descriptor.CollectionPath + "/" + name);
    }

    /// <summary>
    ///     Builds the target of a resource collection.
    /// </summary>
    /// <param name="request">The current request.</param>
    /// <param name="registration">The registration owning the collection.</param>
    /// <param name="filter">An optional AIP filter expression.</param>
    /// <returns>The relative target.</returns>
    public static string Collection(HttpRequest request, ResourceRegistration registration, string? filter = null) {
        var descriptor = ResourceNameDescriptor.ForType(registration.Entity);
        var query      = filter is null ? QueryString.Empty : QueryString.Create("filter", filter);

        return UriHelper.BuildRelative(request.PathBase,
                                       new PathString(LinkTargets.EncodePath(Prefix(registration) + descriptor.CollectionPath)),
                                       query);
    }

    /// <summary>
    ///     Reports whether a registration is published over HTTP.
    /// </summary>
    /// <param name="resource">The registration.</param>
    /// <returns><see langword="true" /> when the registration publishes an HTTP endpoint.</returns>
    public static bool IsPublished(ResourceRegistration resource) =>
        resource.Endpoints is null || resource.Endpoints.Contains(HttpResourceAttribute.Name);

    /// <summary>
    ///     Reports whether a registration permits listing.
    /// </summary>
    /// <param name="resource">The registration.</param>
    /// <returns><see langword="true" /> when <see cref="Operations.List" /> is enabled.</returns>
    public static bool AllowsList(ResourceRegistration resource) =>
        resource.Operations is null || resource.Operations.Contains(Operations.List);
}
