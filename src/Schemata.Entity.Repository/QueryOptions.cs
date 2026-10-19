namespace Schemata.Entity.Repository;

/// <summary>
///     Per-operation query behavior overrides.
/// </summary>
/// <remarks>
///     <see cref="IgnoreSoftDelete" />, <see cref="IgnoreOwner" />, and <see cref="IgnoreCache" />
///     apply to this call only, so nested and parallel queries never inherit them.
///     <see cref="IgnoreCacheEviction" /> applies to the next commit of the repository's current
///     unit of work: it records a commit-scoped request instead of changing the query itself.
/// </remarks>
public readonly record struct QueryOptions(
    bool IgnoreSoftDelete = false,
    bool IgnoreOwner = false,
    bool IgnoreCache = false,
    bool IgnoreCacheEviction = false);
