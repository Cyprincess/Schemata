using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;

namespace Schemata.Entity.Owner.Advisors;

/// <summary>Order constants for <see cref="AdviceBuildQueryOwner{TEntity}" />.</summary>
public static class AdviceBuildQueryOwner
{
    /// <summary>Default execution order.</summary>
    public const int DefaultOrder = AdviceBuildQuerySoftDelete.DefaultOrder + 10_000_000;
}

/// <summary>
///     Applies a global query filter that restricts results to entities owned by the current caller, as
///     reported by <see cref="IOwnerResolver{TEntity}" />.
/// </summary>
/// <typeparam name="TEntity">The entity type being queried.</typeparam>
/// <remarks>
///     <para>Only activates when <typeparamref name="TEntity" /> implements <see cref="IOwnable" />.</para>
///     <para>Suppressed when <see cref="QueryOwnerSuppressed" /> is present in the advice context.</para>
///     <para>
///         When the resolver returns <see langword="null" />, behavior is governed by
///         <see cref="SchemataOwnerOptions.OnNullOwner" /> (default: reject).
///     </para>
/// </remarks>
public sealed class AdviceBuildQueryOwner<TEntity> : IRepositoryBuildQueryAdvisor<TEntity>
    where TEntity : class
{
    private readonly IOptions<SchemataOwnerOptions> _options;
    private readonly IOwnerResolver<TEntity>        _resolver;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AdviceBuildQueryOwner{TEntity}" /> class.
    /// </summary>
    public AdviceBuildQueryOwner(IOwnerResolver<TEntity> resolver, IOptions<SchemataOwnerOptions> options) {
        _resolver = resolver;
        _options  = options;
    }

    #region IRepositoryBuildQueryAdvisor<TEntity> Members

    public int Order => AdviceBuildQueryOwner.DefaultOrder;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext           ctx,
        QueryContainer<TEntity> container,
        CancellationToken       ct = default
    ) {
        if (ctx.Has<QueryOwnerSuppressed>()) {
            return AdviseResult.Continue;
        }

        if (!typeof(IOwnable).IsAssignableFrom(typeof(TEntity))) {
            return AdviseResult.Continue;
        }

        var owner = await _resolver.ResolveAsync(ct);
        if (!string.IsNullOrEmpty(owner)) {
            container.ApplyModification(q => q.OfType<IOwnable>().Where(e => e.Owner == owner).OfType<TEntity>());
            return AdviseResult.Continue;
        }

        switch (_options.Value.OnNullOwner) {
            case OnNullOwnerPolicy.Reject:
                throw new PermissionDeniedException();
            case OnNullOwnerPolicy.EmptyResult:
                return AdviseResult.Block;
            case OnNullOwnerPolicy.AllowAll:
            default:
                return AdviseResult.Continue;
        }
    }

    #endregion
}
