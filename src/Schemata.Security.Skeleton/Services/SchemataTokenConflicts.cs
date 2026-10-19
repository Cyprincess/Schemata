using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Errors;
using Schemata.Common;
using Schemata.Security.Skeleton.Entities;

namespace Schemata.Security.Skeleton.Services;

/// <summary>
///     Classifies <see cref="AbortedException" />s raised by token stores: an abort whose
///     <see cref="ResourceInfoDetail" /> details reference the <see cref="SchemataToken" />
///     resource is a lost write race on a token row (unique index or timestamp CAS), while
///     every other abort belongs to its caller and must propagate.
/// </summary>
public static class SchemataTokenConflicts
{
    /// <summary>Whether <paramref name="exception" /> reports a concurrency conflict on a token row.</summary>
    /// <param name="exception">The abort thrown by a token-store write.</param>
    /// <returns><c>true</c> when the details identify the <see cref="SchemataToken" /> resource.</returns>
    public static bool IsConflict(AbortedException exception) {
        if (exception.Details is null) return false;
        var found = false;
        foreach (var detail in exception.Details) {
            if (detail is not ResourceInfoDetail resource) continue;
            if (resource.ResourceType != ResourceNameDescriptor.ForType<SchemataToken>().Singular) return false;
            found = true;
        }
        return found;
    }
}
