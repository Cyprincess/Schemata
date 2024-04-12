using System;
using System.Collections.Concurrent;
using Schemata.Abstractions;

namespace Schemata.Workflow.Skeleton;

public sealed class TypeResolver : ITypeResolver
{
    private readonly ConcurrentDictionary<string, Type> _types = [];

    #region ITypeResolver Members

    public Type ResolveType(string? name) {
        if (TryResolveType(name, out var result)) {
            return result!;
        }

        throw new TypeAccessException($"Type {name} not found.");
    }

    public bool TryResolveType(string? name, out Type? type) {
        if (string.IsNullOrWhiteSpace(name)) {
            throw new ArgumentNullException(nameof(name));
        }

        if (_types.TryGetValue(name!, out type)) {
            return true;
        }

        type = Type.GetType(name, false);
        if (type != null) {
            _types.TryAdd(name!, type);
            return true;
        }

        return AppDomainTypeCache.Types.TryGetValue(name!, out type);
    }

    #endregion
}
