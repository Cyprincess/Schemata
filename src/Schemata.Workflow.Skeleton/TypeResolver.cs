using System;
using Schemata.Abstractions;

namespace Schemata.Workflow.Skeleton;

public sealed class TypeResolver : ITypeResolver
{
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

        type = AppDomainTypeCache.GetType(name!);
        return type is not null;
    }

    #endregion
}
