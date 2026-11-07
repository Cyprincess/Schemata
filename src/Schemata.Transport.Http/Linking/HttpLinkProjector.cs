using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Linking;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Core.Building;
using Schemata.Core.Linking;
using Schemata.Common.Linking;

namespace Schemata.Transport.Http.Linking;

public sealed class HttpLinkProjector(IEnumerable<ResourceRegistry> registries, IEnumerable<ILinkRouteProvider> routes)
{
    private ResourceRegistry? Registry => registries.SingleOrDefault();
    public LinkContext CreateContext(HttpRequest request, object? result, ResourceRegistration? resource) {
        var input = new QueryInput(request);
        SemanticTarget? target = null;
        if (resource is not null && ResourceLinkTargets.IsPublished(resource)) {
            var prefix = ResourceLinkTargets.Prefix(resource);
            var path = request.Path.Value;
            if (path is not null && path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) {
                var relative = Uri.UnescapeDataString(path[prefix.Length..]);
                var colon = relative.LastIndexOf(':');
                var name = colon < 0 ? relative : relative[..colon];
                var verb = colon < 0 ? null : relative[(colon + 1)..];
                var descriptor = ResourceNameDescriptor.ForType(resource.Entity);
                var slash = name.LastIndexOf('/');
                var parent = descriptor.HasParent && slash > 0 ? name[..slash] : null;
                var collection = descriptor.HasParent ? parent + "/" + descriptor.Collection : descriptor.CollectionPath;
                if (result is IEntitiesResult && verb is null && HttpMethods.IsGet(request.Method)
                 && string.Equals(name, collection, StringComparison.OrdinalIgnoreCase))
                    target = new ResourceCollectionTarget(resource.Entity, parent,
                        input.GetInput("filter"), request.Query.ToDictionary(pair => pair.Key, pair => (string?)pair.Value.ToString()));
                else if (verb is not null)
                    target = new ResourceInstanceTarget(resource.Entity, name, verb);
                else if (result is ICanonicalName { CanonicalName: { Length: > 0 } canonical })
                    target = new ResourceInstanceTarget(resource.Entity, canonical);
            }
        }
        if (target is null) {
            foreach (var route in routes) {
                if (route.GetOperation(request) is { } operation) {
                    target = new DomainOperationTarget(operation,
                        request.Query.ToDictionary(pair => pair.Key, pair => (string?)pair.Value.ToString()));
                    break;
                }
            }
        }
        var context = new LinkContext(target, result, resource, input);
        return target is not null && Project(new("self", target), context, request) is null
            ? context with { Target = null } : context;
    }

    public WebLink? Project(SemanticLink link, LinkContext context, HttpRequest request) {
        string? path;
        switch (link.Target) {
            case ResourceInstanceTarget instance:
                var registration = Registry is not { } registry ? null : instance.Entity is { } type
                    ? ResourceLinkTargets.Find(registry, type) : ResourceLinkTargets.Match(registry, instance.CanonicalName);
                if (registration is null) return null;
                if (ResourceNameDescriptor.ForType(registration.Entity).ParseCanonicalName(instance.CanonicalName) is null) return null;
                if (instance.Verb is { } verb) {
                    if (!registration.Methods.Any(method => method.Verb == verb && method.Scope == ResourceMethodScope.Instance)) return null;
                } else if (registration.Operations is { } operations && !operations.Contains(Operations.Get)) return null;
                path = ResourceLinkTargets.Canonical(request, registration, instance.CanonicalName);
                if (instance.Verb is not null) path += ":" + instance.Verb;
                break;
            case ResourceCollectionTarget collection:
                var candidate = collection.Entity is { } entity && Registry is { } resources
                    ? ResourceLinkTargets.FindCollection(resources, entity) : null;
                if (candidate is null) return null;
                var descriptor = ResourceNameDescriptor.ForType(candidate.Entity);
                if (descriptor.HasParent && descriptor.ParseParent(collection.Parent) is null) return null;
                var name = descriptor.HasParent ? collection.Parent + "/" + descriptor.Collection : descriptor.CollectionPath;
                path = ResourceLinkTargets.Canonical(request, candidate, name);
                break;
            case DomainOperationTarget operation:
                path = routes.Select(route => route.GetRoute(operation.Operation)).FirstOrDefault(route => route is not null);
                if (path is null) return null;
                path = UriHelper.BuildRelative(request.PathBase, new PathString(path));
                break;
            default:
                return null;
        }
        var parameters = link.Target.Parameters;
        if (link.Target is ResourceCollectionTarget { Filter: { } filter }) {
            var values = parameters is null ? new Dictionary<string, string?>() : new Dictionary<string, string?>(parameters);
            values["filter"] = filter;
            parameters = values;
        }
        var query = parameters?.Where(pair => pair.Value is not null && pair.Key != "page_token");
        if (link.Target is DomainOperationTarget) {
            if (query is not null) path += QueryString.Create(query);
            var attributes = parameters?.Where(pair => pair.Value is not null && pair.Key == "page_token")
                .ToDictionary(pair => pair.Key, pair => pair.Value ?? string.Empty);
            var projected = new WebLink(path, link.Relation, Title: link.Title, Extensions: attributes);
            return LinkValue.IsRepresentable(projected) ? projected : null;
        }
        if (parameters is not null) path += QueryString.Create(parameters.Where(pair => pair.Value is not null));
        var resourceLink = new WebLink(path, link.Relation, Title: link.Title);
        return LinkValue.IsRepresentable(resourceLink) ? resourceLink : null;
    }

    private sealed class QueryInput(HttpRequest request) : ILinkRequest
    {
        public string? GetInput(string name) => request.Query.TryGetValue(name, out var value) ? value.ToString() : null;
    }
}
