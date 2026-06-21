using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Resource;
using Schemata.Common;

namespace Schemata.Resource.Foundation;

/// <summary>
///     Reverse-resolves an entity type from a resource name against the registered
///     <see cref="SchemataResourceOptions.Resources" />. A collection index is built once on first
///     use; <see cref="Resolve" /> additionally matches a full canonical name against each registered
///     resource pattern.
/// </summary>
public sealed class DefaultResourceTypeResolver : IResourceTypeResolver
{
    private readonly Lazy<Dictionary<string, Type>>   _byCollection;
    private readonly IOptions<SchemataResourceOptions> _options;

    /// <summary>Creates the resolver over the registered resource descriptors.</summary>
    /// <param name="options">The registered resources.</param>
    public DefaultResourceTypeResolver(IOptions<SchemataResourceOptions> options) {
        _options      = options;
        _byCollection = new(BuildCollectionIndex);
    }

    #region IResourceTypeResolver Members

    public Type? Resolve(string canonicalName) {
        if (string.IsNullOrWhiteSpace(canonicalName)) {
            return null;
        }

        foreach (var resource in _options.Value.Resources.Values) {
            var entity = resource.Entity;
            if (ResourceNameDescriptor.ForType(entity).ParseCanonicalName(canonicalName) is not null) {
                return entity;
            }
        }

        return ResolveCollection(canonicalName);
    }

    public Type? ResolveCollection(string collection) {
        if (string.IsNullOrEmpty(collection)) {
            return null;
        }

        return _byCollection.Value.TryGetValue(collection, out var entity) ? entity : null;
    }

    #endregion

    private Dictionary<string, Type> BuildCollectionIndex() {
        var index = new Dictionary<string, Type>(StringComparer.Ordinal);
        foreach (var resource in _options.Value.Resources.Values) {
            var entity     = resource.Entity;
            var collection = ResourceNameDescriptor.ForType(entity).Collection;
            if (!string.IsNullOrEmpty(collection)) {
                index.TryAdd(collection, entity);
            }
        }

        return index;
    }
}
