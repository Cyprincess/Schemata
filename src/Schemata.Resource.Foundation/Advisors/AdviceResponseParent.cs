using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Default order constants for <see cref="AdviceResponseParent{TEntity, TDetail}" />.
/// </summary>
public static class AdviceResponseParent
{
    /// <summary>
    ///     Default order at <see cref="SchemataConstants.Orders.Base" />.
    /// </summary>
    public const int DefaultOrder = SchemataConstants.Orders.Base;
}

/// <summary>
///     Derives <see cref="IChild.Parent" /> on the response detail from the entity's
///     <see cref="ICanonicalName.CanonicalName" /> for Get / Create / Update.
///     Falls back to the detail's own canonical name when the entity is unavailable.
/// </summary>
/// <remarks>
///     Uses an Ensure pattern: if <see cref="IChild.Parent" /> already holds the
///     derived value, the advisor leaves it untouched.
/// </remarks>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TDetail">The detail DTO type; the advisor fires only when it implements <see cref="IChild" />.</typeparam>
public sealed class AdviceResponseParent<TEntity, TDetail> : IResourceResponseAdvisor<TEntity, TDetail>
    where TEntity : class, ICanonicalName
    where TDetail : class, ICanonicalName
{
    /// <inheritdoc cref="AdviceResponseParent" />
    public int Order => AdviceResponseParent.DefaultOrder;

    #region IResourceResponseAdvisor<TEntity,TDetail> Members

    public Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        TEntity?          entity,
        TDetail?          detail,
        ClaimsPrincipal?  principal,
        CancellationToken ct = default
    ) {
        if (detail is not IChild child) {
            return Task.FromResult(AdviseResult.Continue);
        }

        var source = entity?.CanonicalName ?? detail.CanonicalName;
        var parent = ChildParentHelper.DeriveParent(source);

        if (!string.Equals(child.Parent, parent, StringComparison.Ordinal)) {
            child.Parent = parent;
        }

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
