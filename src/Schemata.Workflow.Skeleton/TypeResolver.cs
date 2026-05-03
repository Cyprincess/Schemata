using System;
using Schemata.Abstractions;
using Schemata.Common;

namespace Schemata.Workflow.Skeleton;

/// <summary>
///     Default implementation of <see cref="ITypeResolver" /> that resolves types from the application domain type cache.
/// </summary>
public sealed class TypeResolver : ITypeResolver
{
    #region ITypeResolver Members

    /// <inheritdoc />
    public Type ResolveType(string? name) {
        if (TryResolveType(name, out var result)) {
            return result!;
        }

        throw new TypeAccessException(
            string.Format(SchemataResources.GetResourceString(SchemataResources.ST1011), "Type", name)
        );
    }

    /// <inheritdoc />
    public bool TryResolveType(string? name, out Type? type) {
        if (string.IsNullOrWhiteSpace(name)) {
            throw new ArgumentNullException(nameof(name));
        }

        type = AppDomainTypeCache.GetType(name!);
        return type is not null;
    }

    #endregion
}
