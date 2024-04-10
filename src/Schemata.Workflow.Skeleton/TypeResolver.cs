using System;
using System.Collections.Concurrent;

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

        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        foreach (var assembly in assemblies) {
            type = assembly.GetType(name, false);
            if (type == null) {
                continue;
            }

            _types.TryAdd(name!, type);
            return true;
        }

        return false;
    }

    #endregion
}
