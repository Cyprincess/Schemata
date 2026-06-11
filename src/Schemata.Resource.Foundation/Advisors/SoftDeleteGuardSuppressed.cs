namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Context marker disabling <see cref="AdviceUpdateSoftDeleted{TEntity, TRequest}" />
///     so restoration flows (e.g. undelete) can mutate a soft-deleted entity.
/// </summary>
public sealed class SoftDeleteGuardSuppressed;
