using System;
using System.Collections.Generic;
using Schemata.Abstractions.Resource;

namespace Schemata.Resource.Foundation;

/// <summary>
///     Default <see cref="IResourceRegistry" />. Written through <see cref="Add" /> while services are
///     being registered; the first read seals it, so a resource cannot appear after the container is
///     built and every consumer observes the same set.
/// </summary>
public sealed class ResourceRegistry : IResourceRegistry
{
    private readonly Dictionary<RuntimeTypeHandle, List<ResourceMethodAttribute>> _methods   = [];
    private readonly List<ResourceAttribute>                                      _ordered   = [];
    private readonly Dictionary<RuntimeTypeHandle, ResourceAttribute>             _resources = [];

    private bool _sealed;

    #region IResourceRegistry Members

    public IReadOnlyList<ResourceAttribute> Resources {
        get {
            _sealed = true;
            return _ordered;
        }
    }

    public ResourceAttribute? GetResource(Type entity) {
        _sealed = true;
        return _resources.GetValueOrDefault(entity.TypeHandle);
    }

    public IReadOnlyList<ResourceMethodAttribute> GetMethods(Type entity) {
        _sealed = true;
        return _methods.TryGetValue(entity.TypeHandle, out var methods) ? methods : [];
    }

    #endregion

    /// <summary>
    ///     Registers <paramref name="resource" /> and its custom methods. Registering the same entity
    ///     again merges instead of replacing: a <see langword="null" /> endpoint list on either side
    ///     means "every endpoint" and wins, otherwise the lists union; methods merge by verb.
    /// </summary>
    public void Add(ResourceAttribute resource, IReadOnlyList<ResourceMethodAttribute> methods) {
        if (_sealed) {
            throw new InvalidOperationException(
                $"Resource '{resource.Entity.FullName}' cannot be registered after the registry has been read. "
                + "Register every resource while configuring services.");
        }

        var handle = resource.Entity.TypeHandle;
        if (!_resources.TryGetValue(handle, out var existing)) {
            _resources[handle] = resource;
            _ordered.Add(resource);
        } else if (existing.Endpoints is null || resource.Endpoints is null) {
            existing.Endpoints = null;
        } else {
            foreach (var endpoint in resource.Endpoints) {
                if (!existing.Endpoints.Contains(endpoint)) {
                    existing.Endpoints.Add(endpoint);
                }
            }
        }

        if (methods.Count == 0) {
            return;
        }

        if (!_methods.TryGetValue(handle, out var declared)) {
            _methods[handle] = [..methods];
            return;
        }

        var byVerb = new Dictionary<string, ResourceMethodAttribute>(StringComparer.Ordinal);
        foreach (var method in declared) {
            byVerb[method.Verb] = method;
        }

        foreach (var method in methods) {
            byVerb[method.Verb] = method;
        }

        declared.Clear();
        declared.AddRange(byVerb.Values);
    }
}
