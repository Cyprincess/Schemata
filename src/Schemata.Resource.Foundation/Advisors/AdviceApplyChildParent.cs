using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;
using Schemata.Common;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Default order constants for <see cref="AdviceApplyChildParent{TEntity, TRequest}" />.
/// </summary>
public static class AdviceApplyChildParent
{
    /// <summary>
    ///     Default order anchored at <see cref="SchemataConstants.Orders.Base" /> so the
    ///     mode A parent field is populated before <see cref="AdviceUpdateSoftDeleted" />
    ///     and <see cref="AdviceUpdateFreshness" />, both of which chain off this constant.
    /// </summary>
    public const int DefaultOrder = SchemataConstants.Orders.Base;
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
///         <see cref="SchemataResources.CROSS_PARENT_UNSUPPORTED" />. A parent path
///         that does not match the resource's
///         <see cref="ResourceNameDescriptor.Pattern" /> is rejected with
///         <see cref="SchemataResources.INVALID_PARENT" />.
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
                    new() {
                        Field       = nameof(IChild.Parent).ToLowerInvariant(),
                        Description = LocalizedMessageFormatter.FormatInvariant(
                            SchemataResources.CROSS_PARENT_UNSUPPORTED,
                            new Dictionary<string, string> { ["parent"] = child.Parent! }),
                        Reason      = SchemataResources.CROSS_PARENT_UNSUPPORTED,
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
                new() {
                    Field       = nameof(IChild.Parent).ToLowerInvariant(),
                    Description = LocalizedMessageFormatter.FormatInvariant(
                        SchemataResources.INVALID_PARENT,
                        new Dictionary<string, string> { ["parent"] = child.Parent! }),
                    Reason      = SchemataResources.INVALID_PARENT,
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
