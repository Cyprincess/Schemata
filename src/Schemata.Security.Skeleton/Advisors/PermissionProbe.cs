using System;
using Schemata.Common.Errors;

namespace Schemata.Security.Skeleton.Advisors;

/// <summary>
///     Builds the AIP-211 failure for a coarse permission denial: every authorization failure is a
///     PERMISSION_DENIED carrying the request name, resource type, and the missing permission. A
///     same-entity Get probe is not an AIP-211 parent-resource check and is never performed; NOT_FOUND
///     belongs to lookup misses that occur after authorization succeeds.
/// </summary>
internal static class PermissionProbe
{
    public static Exception Create(
        string  operation,
        Type    entity,
        string? permission,
        string? name
    ) {
        return SchemataResourceErrors.PermissionDenied(
            entity,
            name,
            description: string.Format(
                SchemataResourceErrors.PermissionDeniedTemplate,
                permission ?? operation,
                name ?? entity.Name));
    }
}
