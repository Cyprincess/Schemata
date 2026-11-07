using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Errors;
using Schemata.Common;
using Schemata.Security.Skeleton.Entities;

namespace Schemata.Security.Skeleton.Services;

/// <summary>
///     Classifies <see cref="AbortedException" />s raised by token stores. A
///     <see cref="ResourceInfoDetail" /> matching the actual token resource type identifies a lost
///     token write race; every other abort belongs to its caller and must propagate.
/// </summary>
public static class SchemataTokenConflicts
{
    /// <summary>Whether <paramref name="exception" /> reports a concurrency conflict on a token row.</summary>
    /// <param name="exception">The abort thrown by a token-store write.</param>
    /// <typeparam name="TToken">The actual token entity type.</typeparam>
    /// <returns><c>true</c> when the details identify the <typeparamref name="TToken" /> resource.</returns>
    public static bool IsConflict<TToken>(AbortedException exception) where TToken : SchemataToken {
        if (exception.Details is not { } details) return false;
        var descriptor = ResourceNameDescriptor.ForType<TToken>();
        var expected = descriptor.IsAddressable ? descriptor.Singular : typeof(TToken).Name;
        var found = false;
        foreach (var detail in details) {
            if (detail is not ResourceInfoDetail resource) continue;
            if (resource.ResourceType != expected) return false;
            found = true;
        }
        return found;
    }
}
