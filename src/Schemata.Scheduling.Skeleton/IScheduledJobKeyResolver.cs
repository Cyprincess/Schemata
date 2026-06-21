using System;

namespace Schemata.Scheduling.Skeleton;

/// <summary>
///     Resolves scheduler keys for job types that the registry cannot key by type name or
///     <c>[ScheduledJob]</c> attribute, such as closed-generic jobs (for example a per-resource purge
///     job) whose stable key encodes a runtime value. A module that owns such jobs contributes a
///     resolver; the registry consults every resolver on a lookup miss and caches the result.
/// </summary>
public interface IScheduledJobKeyResolver
{
    /// <summary>
    ///     Maps a stable key to its concrete (possibly closed-generic) job type, or
    ///     <see langword="null" /> when this resolver does not own the key.
    /// </summary>
    Type? ResolveType(string key);

    /// <summary>
    ///     Maps a concrete (possibly closed-generic) job type to its stable key, or
    ///     <see langword="null" /> when this resolver does not own the type.
    /// </summary>
    string? ResolveKey(Type jobType);
}
