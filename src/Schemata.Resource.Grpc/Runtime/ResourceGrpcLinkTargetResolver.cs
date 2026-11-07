using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Core.Building;
using Schemata.Core.Linking;
using Schemata.Transport.Grpc.Linking;

namespace Schemata.Resource.Grpc.Runtime;

public sealed class ResourceGrpcLinkTargetResolver(ResourceRegistry registry) : IGrpcLinkTargetResolver
{
    public GrpcLinkTarget? Resolve(SemanticTarget target) {
        var entity = target switch { ResourceInstanceTarget instance => instance.Entity, ResourceCollectionTarget collection => collection.Entity, _ => null };
        var resource = registry.Resources.FirstOrDefault(candidate => candidate.IsEndpointEnabled(GrpcResourceAttribute.Name)
            && (entity is not null ? entity.IsAssignableFrom(candidate.Entity)
                : target is ResourceInstanceTarget instance && Matches(candidate, instance.CanonicalName)));
        if (resource is null) return null;
        var descriptor = ResourceNameDescriptor.ForType(resource.Entity);
        if (!descriptor.IsAddressable) return null;
        var inputs = target.Parameters is null ? new Dictionary<string, string?>(StringComparer.Ordinal)
            : new Dictionary<string, string?>(target.Parameters, StringComparer.Ordinal);
        string method;
        switch (target) {
            case ResourceInstanceTarget instance:
                if (!Matches(resource, instance.CanonicalName)) return null;
                inputs["name"] = instance.CanonicalName;
                if (instance.Verb is { } verb) {
                    if (!resource.Methods.Any(candidate => candidate.Verb == verb && candidate.Scope == ResourceMethodScope.Instance)) return null;
                    method = GrpcResourceNaming.CustomMethodName(descriptor, verb);
                } else {
                    if (resource.Operations is { } operations && !operations.Contains(Operations.Get)) return null;
                    method = GrpcResourceNaming.MethodName(descriptor, Operations.Get);
                }
                break;
            case ResourceCollectionTarget collection:
                if (resource.Operations is { } allowed && !allowed.Contains(Operations.List)) return null;
                if (descriptor.HasParent && descriptor.ParseParent(collection.Parent) is null) return null;
                if (collection.Parent is not null) inputs["parent"] = collection.Parent;
                if (collection.Filter is not null) inputs["filter"] = collection.Filter;
                method = GrpcResourceNaming.MethodName(descriptor, Operations.List);
                break;
            default:
                return null;
        }
        return new($"/{GrpcResourceNaming.ServiceFullName(resource.Entity)}/{method}", inputs);
    }

    public SemanticTarget? GetTarget(string method, object request) {
        foreach (var resource in registry.Resources) {
            if (!resource.IsEndpointEnabled(GrpcResourceAttribute.Name)) continue;
            var descriptor = ResourceNameDescriptor.ForType(resource.Entity);
            if (!descriptor.IsAddressable) continue;
            var prefix = $"/{GrpcResourceNaming.ServiceFullName(resource.Entity)}/";
            if (!method.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var rpc = method[prefix.Length..];
            SemanticTarget? target = null;
            if (request is ListRequest list && rpc == GrpcResourceNaming.MethodName(descriptor, Operations.List)) {
                target = new ResourceCollectionTarget(resource.Entity, list.Parent, list.Filter,
                    new Dictionary<string, string?> {
                        ["page_token"] = list.PageToken, ["page_size"] = list.PageSize?.ToString(CultureInfo.InvariantCulture),
                        ["skip"] = list.Skip?.ToString(CultureInfo.InvariantCulture), ["order_by"] = list.OrderBy,
                        ["language"] = list.Language, ["show_deleted"] = list.ShowDeleted?.ToString().ToLowerInvariant(),
                    });
            } else if (request is ICanonicalName named && (named.CanonicalName ?? named.Name) is { Length: > 0 } name) {
                var verb = resource.Methods.FirstOrDefault(custom => GrpcResourceNaming.CustomMethodName(descriptor, custom.Verb) == rpc)?.Verb;
                if (rpc == GrpcResourceNaming.MethodName(descriptor, Operations.Get) || verb is not null)
                    target = new ResourceInstanceTarget(resource.Entity, name, verb);
            }
            return target is not null && Resolve(target) is not null ? target : null;
        }
        return null;
    }

    private static bool Matches(ResourceRegistration resource, string name) =>
        ResourceNameDescriptor.ForType(resource.Entity).ParseCanonicalName(name) is not null;
}
