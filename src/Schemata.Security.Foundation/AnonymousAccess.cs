using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using Schemata.Abstractions.Resource;

namespace Schemata.Security.Foundation;

/// <summary>
///     Checks whether an entity type's [Anonymous] attribute allows anonymous access for a given operation.
/// </summary>
public static class AnonymousAccess
{
    private static readonly ConcurrentDictionary<RuntimeTypeHandle, AnonymousAttribute?> Cache = new();

    /// <summary>Determines whether an entity type permits anonymous access for an operation.</summary>
    /// <typeparam name="TEntity">Entity type to inspect.</typeparam>
    /// <param name="operation">Operation name to match against the attribute.</param>
    /// <returns><see langword="true"/> when anonymous access is permitted; otherwise, <see langword="false"/>.</returns>
    public static bool IsAnonymous<TEntity>(string operation) { return IsAnonymous(typeof(TEntity), operation); }

    /// <summary>Determines whether an entity type permits anonymous access for an operation.</summary>
    /// <param name="entity">Entity type to inspect.</param>
    /// <param name="operation">Operation name to match against the attribute.</param>
    /// <returns><see langword="true"/> when anonymous access is permitted; otherwise, <see langword="false"/>.</returns>
    public static bool IsAnonymous(Type entity, string operation) {
        var attribute = Cache.GetOrAdd(entity.TypeHandle, _ => entity.GetCustomAttribute<AnonymousAttribute>());
        if (attribute is null) {
            return false;
        }

        if (attribute.Operations is null or { Length: 0 }) {
            return true;
        }

        return attribute.Operations.Any(op => string.Equals(op, operation, StringComparison.OrdinalIgnoreCase));
    }
}
