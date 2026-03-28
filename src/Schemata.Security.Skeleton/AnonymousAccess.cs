using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using Schemata.Abstractions.Resource;

namespace Schemata.Security.Skeleton;

/// <summary>
///     Checks whether an entity type's [Anonymous] attribute allows anonymous access for a given operation.
/// </summary>
public static class AnonymousAccess
{
    private static readonly ConcurrentDictionary<RuntimeTypeHandle, AnonymousAttribute?> Cache = new();

    public static bool IsAnonymous<TEntity>(string operation) { return IsAnonymous(typeof(TEntity), operation); }

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
