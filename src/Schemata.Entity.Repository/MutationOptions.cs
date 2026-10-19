namespace Schemata.Entity.Repository;

/// <summary>
///     Per-operation mutation behavior overrides.
/// </summary>
/// <remarks>
///     <see cref="IgnoreValidation" />, <see cref="IgnoreSoftDelete" />, and
///     <see cref="IgnoreUniqueness" /> apply to this call only, so nested and parallel mutations
///     never inherit them. <see cref="IgnoreCacheEviction" /> applies to the next commit of the
///     repository's current unit of work: it records a commit-scoped request instead of changing
///     the mutation itself.
/// </remarks>
public readonly record struct MutationOptions(
    bool IgnoreValidation = false,
    bool IgnoreSoftDelete = false,
    bool IgnoreUniqueness = false,
    bool IgnoreCacheEviction = false);
