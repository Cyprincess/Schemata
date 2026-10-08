using System;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Security.Skeleton;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Shared response shaping for the Get / Create / Update detail wraps: derives
///     <see cref="IChild.Parent" /> from the detail's own canonical name, then sets the
///     <see cref="IFreshness.EntityTag" /> through <see cref="IEntityTagProvider" />.
/// </summary>
public static class ResourceDetailResponsePipelineAdvisor
{
    /// <summary>
    ///     Default order: one slot above <see cref="SecurityOrders.ResponseFamily" /> so the detail wrap
    ///     sits behind the list wrap, and above <see cref="SecurityOrders.Idempotency" /> so a later
    ///     idempotency wrap commits the shaped payload.
    /// </summary>
    public const int DefaultOrder = SecurityOrders.ResponseFamily + 10_000_000;

    /// <summary>
    ///     Shapes one detail in place: parent first, then the ETag. The ETag is emitted only when an
    ///     <see cref="IEntityTagProvider" /> is installed — the same installation decision that
    ///     governs the freshness check stages — and freshness is not suppressed for the dispatch.
    /// </summary>
    /// <typeparam name="TEntity">The entity type behind the response.</typeparam>
    /// <typeparam name="TDetail">The detail DTO type carrying the response.</typeparam>
    /// <param name="ctx">The ambient advisor context for the dispatch.</param>
    /// <param name="detail">The mapped detail, or <see langword="null" /> when the response carries none.</param>
    public static void Shape<TEntity, TDetail>(AdviceContext ctx, TDetail? detail)
        where TEntity : class, ICanonicalName
        where TDetail : class, ICanonicalName {
        if (detail is null) {
            return;
        }

        if (detail is IChild child) {
            var parent = ChildParentHelper.DeriveParent(detail.CanonicalName);
            if (!string.Equals(child.Parent, parent, StringComparison.Ordinal)) {
                child.Parent = parent;
            }
        }

        if (ctx.Has<FreshnessSuppressed>() || detail is not IFreshness freshness) {
            return;
        }

        var entityTags = ctx.ServiceProvider.GetService<IEntityTagProvider>();
        if (entityTags is null) {
            return;
        }

        var tag = entityTags.GetEntityTag<TEntity, TDetail>(detail, ctx);
        if (tag is not null) {
            freshness.EntityTag = tag;
        }
    }
}