using System;
using System.Collections.Generic;
using System.Linq;
using Schemata.Abstractions;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Core.Building;
using static Schemata.Abstractions.SchemataConstants.LinkRelations;

namespace Schemata.Core.Linking;

public sealed class ResourceLinkSemantics(ResourceRegistry registry, IResourceLinkPolicy policy)
{
    public IEnumerable<SemanticLink> GetLinks(LinkContext context) {
        if (context.Resource is not { } resource || context.Target is null || context.Result is null) yield break;
        if (context.Result is IEntitiesResult page && context.Target is ResourceCollectionTarget collection) {
            yield return new(Self, collection);
            if (page.NextPageToken is { Length: > 0 } nextToken)
                yield return new(Next, WithPage(collection, nextToken));
            var current = policy.ReadPage(context.Request?.GetInput(SchemataConstants.Pagination.PageToken));
            var next = policy.ReadPage(page.NextPageToken);
            if (current is not null) {
                if (policy.WritePage(current, 0) is { } first)
                    yield return new(First, WithPage(collection, first));
                if (current.PageSize > 0 && current.Skip > 0
                 && policy.WritePage(current, Math.Max(current.Skip - current.PageSize, 0)) is { } previous)
                    yield return new(Prev, WithPage(collection, previous));
            }
            var basis = current ?? next;
            if (policy.EffectiveTotalSize(resource) is not TotalSizeMode.Estimated
             && basis is { PageSize: > 0 } && page.TotalSize is > 0) {
                var offset = current?.Skip ?? Math.Max((next?.Skip ?? 0) - basis.PageSize, 0);
                var last = ((page.TotalSize.Value - 1) / basis.PageSize) * basis.PageSize;
                if (last > offset && policy.WritePage(basis, last) is { } token)
                    yield return new(Last, WithPage(collection, token));
            }
            yield break;
        }
        if (context.Result is not ICanonicalName { CanonicalName: { Length: > 0 } name }) yield break;
        var owner = registry.Resources.FirstOrDefault(candidate =>
            ResourceNameDescriptor.ForType(candidate.Entity).ParseCanonicalName(name) is not null);
        if (owner is null) yield break;
        var canonical = new ResourceInstanceTarget(owner.Entity, name);
        yield return new(Self, context.Target is ResourceInstanceTarget { Verb: null, Entity: { } entity }
            && entity == owner.Entity ? context.Target : canonical);
        yield return new(Canonical, canonical);
        var descriptor = ResourceNameDescriptor.ForType(owner.Entity);
        if (!descriptor.IsAddressable) yield break;
        if (descriptor.ParentOf(name) is { } parent)
            yield return new(Up, new ResourceInstanceTarget(null, parent));
        if (descriptor.Pattern is not { } pattern) yield break;
        var parts = pattern.Split('/');
        foreach (var candidate in registry.Resources) {
            var child = ResourceNameDescriptor.ForType(candidate.Entity);
            if (candidate.Entity == owner.Entity || !child.IsAddressable || child.Pattern is not { } childPattern) continue;
            var segments = childPattern.Split('/');
            if (segments.Length != parts.Length + 2 || !Prefix(parts, segments)) continue;
            yield return new(Related, new ResourceCollectionTarget(candidate.Entity, name), child.Plural);
        }
    }

    private static ResourceCollectionTarget WithPage(ResourceCollectionTarget target, string token) {
        var parameters = target.Parameters is null
            ? new Dictionary<string, string?>(StringComparer.Ordinal)
            : new Dictionary<string, string?>(target.Parameters, StringComparer.Ordinal);
        parameters.Remove(SchemataConstants.Pagination.Skip);
        parameters.Remove("page_size");
        parameters[SchemataConstants.Pagination.PageToken] = token;
        return target with { Parameters = parameters };
    }

    private static bool Prefix(string[] parent, string[] child) {
        for (var index = 0; index < parent.Length; index++) {
            if (ResourceNameDescriptor.IsPlaceholder(parent[index]) && ResourceNameDescriptor.IsPlaceholder(child[index])) continue;
            if (!string.Equals(parent[index], child[index], StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }
}
