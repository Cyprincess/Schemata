using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;
using Schemata.Common;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Default order constants for <see cref="AdviceApplyChildParent{TEntity, TRequest}" />.
/// </summary>
public static class AdviceApplyChildParent
{
    /// <summary>
    ///     Default order: runs early in both the Create and Update entity chains so the
    ///     mode A parent field is populated before later advisors inspect or persist it.
    /// </summary>
    public const int DefaultOrder = AdviceUpdateSoftDeleted.DefaultOrder - 1_000_000;
}

/// <summary>
///     Reverse-parses <see cref="IChild.Parent" /> on the request DTO and writes the
///     resulting parent leaf id(s) back onto the entity's mode A structural parent
///     field(s) before persistence.
/// </summary>
/// <remarks>
///     <para>
///         Non-<see cref="IChild" /> requests are a no-op so the advisor is safe to
///         register globally for every <c>(TEntity, TRequest)</c> shape. A blank
///         <see cref="IChild.Parent" /> is also a no-op, leaving the entity's parent
///         field as the HTTP controller already filled it from the route.
///     </para>
///     <para>
///         Wildcard parents (<c>tenants/-</c> per AIP-159) are rejected with a
///         <see cref="ValidationException" /> carrying
///         <see cref="FieldReasons.CrossParentUnsupported" />. A parent path that
///         does not match the resource's
///         <see cref="ResourceNameDescriptor.Pattern" /> is rejected with
///         <see cref="FieldReasons.InvalidParent" />.
///     </para>
/// </remarks>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TRequest">The request DTO type; reverse-parsing fires only when it implements <see cref="IChild" />.</typeparam>
public sealed class AdviceApplyChildParent<TEntity, TRequest> :
    IResourceCreateAdvisor<TEntity, TRequest>,
    IResourceUpdateAdvisor<TEntity, TRequest>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName
{
    /// <inheritdoc cref="AdviceApplyChildParent" />
    public int Order => AdviceApplyChildParent.DefaultOrder;

    #region IResourceCreateAdvisor<TEntity,TRequest> Members

    public Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        TRequest          request,
        TEntity           entity,
        ClaimsPrincipal?  principal,
        CancellationToken ct = default
    ) {
        Apply(request, entity);
        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion

    private static void Apply(TRequest request, TEntity entity) {
        if (request is not IChild child) {
            return;
        }

        if (string.IsNullOrWhiteSpace(child.Parent)) {
            return;
        }

        var descriptor = ResourceNameDescriptor.ForType<TEntity>();
        if (!descriptor.HasParent) {
            return;
        }

        foreach (var segment in child.Parent!.Split('/')) {
            if (segment == "-") {
                throw new ValidationException([
                    new ErrorFieldViolation {
                        Field       = nameof(IChild.Parent).ToLowerInvariant(),
                        Description = $"The parent `{child.Parent}` uses AIP-159 wildcard which is not supported here.",
                        Reason      = FieldReasons.CrossParentUnsupported,
                    },
                ]);
            }
        }

        // ParseCanonicalName expects a full canonical (parent + leaf); a sentinel leaf
        // satisfies the pattern matcher without leaking into the entity.
        var sentinel = $"{child.Parent}/{descriptor.Collection}/_";
        var parsed   = descriptor.ParseCanonicalName(sentinel);
        if (parsed is null) {
            throw new ValidationException([
                new ErrorFieldViolation {
                    Field       = nameof(IChild.Parent).ToLowerInvariant(),
                    Description = $"The parent `{child.Parent}` does not match the resource's pattern.",
                    Reason      = FieldReasons.InvalidParent,
                },
            ]);
        }

        var (parentValues, _) = parsed.Value;

        var routeValues = new Dictionary<string, object?>(parentValues.Count);
        foreach (var kv in parentValues) {
            routeValues[kv.Key] = kv.Value;
        }

        descriptor.SetParentFromRouteValues(entity, routeValues);
    }
}
