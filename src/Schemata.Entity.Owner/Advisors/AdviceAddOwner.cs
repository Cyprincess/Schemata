using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;

namespace Schemata.Entity.Owner.Advisors;

/// <summary>Order constants for <see cref="AdviceAddOwner{TEntity}" />.</summary>
public static class AdviceAddOwner
{
    /// <summary>Default execution order.</summary>
    public const int DefaultOrder = AdviceAddCanonicalName.DefaultOrder + 1_000_000;
}

/// <summary>
///     Populates <see cref="IOwnable.Owner" /> from <see cref="IOwnerResolver{TEntity}" /> when an entity is added.
/// </summary>
/// <typeparam name="TEntity">The entity type being added.</typeparam>
/// <remarks>
///     <para>
///         Runs after <see cref="AdviceAddCanonicalName{TEntity}" /> so the entity's own canonical name is settled
///         first.
///     </para>
///     <para>Only activates when <typeparamref name="TEntity" /> implements <see cref="IOwnable" />.</para>
///     <para>Suppressed when <see cref="OwnerSuppressed" /> is present in the advice context.</para>
///     <para>Leaves an already-set <see cref="IOwnable.Owner" /> untouched so callers can override the default.</para>
///     <para>
///         When the resolver returns <see langword="null" />, behavior is governed by
///         <see cref="SchemataOwnerOptions.OnNullOwner" /> (default: reject).
///     </para>
/// </remarks>
public sealed class AdviceAddOwner<TEntity> : IRepositoryAddAdvisor<TEntity>
    where TEntity : class
{
    private readonly IOptions<SchemataOwnerOptions> _options;
    private readonly IOwnerResolver<TEntity>        _resolver;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AdviceAddOwner{TEntity}" /> class.
    /// </summary>
    public AdviceAddOwner(IOwnerResolver<TEntity> resolver, IOptions<SchemataOwnerOptions> options) {
        _resolver = resolver;
        _options  = options;
    }

    #region IRepositoryAddAdvisor<TEntity> Members

    public int Order => AdviceAddOwner.DefaultOrder;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext        ctx,
        IRepository<TEntity> repository,
        TEntity              entity,
        CancellationToken    ct
    ) {
        if (ctx.Has<OwnerSuppressed>()) {
            return AdviseResult.Continue;
        }

        if (entity is not IOwnable ownable) {
            return AdviseResult.Continue;
        }

        if (!string.IsNullOrEmpty(ownable.Owner)) {
            return AdviseResult.Continue;
        }

        var owner = await _resolver.ResolveAsync(ct);
        if (!string.IsNullOrEmpty(owner)) {
            ownable.Owner = owner;
            return AdviseResult.Continue;
        }

        return _options.Value.OnNullOwner switch {
            OnNullOwnerPolicy.Reject      => throw new PermissionDeniedException(),
            OnNullOwnerPolicy.EmptyResult => AdviseResult.Block,
            OnNullOwnerPolicy.AllowAll    => AdviseResult.Continue,
            var _                         => AdviseResult.Continue,
        };
    }

    #endregion
}
