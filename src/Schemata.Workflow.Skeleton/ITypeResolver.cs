using System;

namespace Schemata.Workflow.Skeleton;

/// <summary>
///     Resolves CLR types by name, used to locate entity types for workflow instances.
/// </summary>
public interface ITypeResolver
{
    /// <summary>
    ///     Resolves a CLR type by its fully qualified name.
    /// </summary>
    /// <param name="name">The fully qualified type name.</param>
    /// <returns>The resolved <see cref="Type" />.</returns>
    /// <exception cref="TypeAccessException">Thrown when the type cannot be found.</exception>
    Type ResolveType(string? name);

    /// <summary>
    ///     Attempts to resolve a CLR type by its fully qualified name.
    /// </summary>
    /// <param name="name">The fully qualified type name.</param>
    /// <param name="type">When this method returns, contains the resolved type, or <see langword="null" /> if not found.</param>
    /// <returns><see langword="true" /> if the type was found; otherwise, <see langword="false" />.</returns>
    bool TryResolveType(string? name, out Type? type);
}
