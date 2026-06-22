using System;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Humanizer;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Mapping.Skeleton;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Order constants and field trimming shared by the
///     <seealso href="https://google.aip.dev/157">AIP-157: Partial responses</seealso> response advisors.
/// </summary>
public static class AdviceResponseReadMask
{
    /// <summary>
    ///     Default order: runs after <see cref="AdviceResponseFreshness{TEntity, TDetail}" />
    ///     so a mask excluding <c>etag</c> trims the freshly computed tag as well.
    /// </summary>
    public const int DefaultOrder = AdviceResponseFreshness.DefaultOrder + 10_000_000;

    /// <summary>
    ///     Clears every writable property on <paramref name="target" /> that the mask does not
    ///     name. Nested paths trim object graphs and collection elements recursively.
    /// </summary>
    /// <typeparam name="T">The DTO type being trimmed.</typeparam>
    /// <param name="target">The DTO instance.</param>
    /// <param name="mask">The comma-separated field paths.</param>
    public static void Trim<T>(T target, string mask) where T : class {
        MaskTree tree;
        try {
            tree = MaskTree.FromWire(typeof(T), mask, true, ResourceWireNameRules.ResolveClrName);
        } catch (ArgumentException ex) {
            throw InvalidReadMaskPath(mask, ex.Message);
        }

        // AIP-157 read masks may traverse repeated fields, so collection elements are trimmed too.
        MaskWalker.WalkUnmasked(target, tree.Children, true, static (_, t, p) => Clear(t, p));
    }

    private static void Clear(object target, PropertyInfo property) {
        var @default = property.PropertyType.IsValueType
            ? Activator.CreateInstance(property.PropertyType)
            : null;
        property.SetValue(target, @default);
    }

    private static ValidationException InvalidReadMaskPath(string path, string reason) {
        return new([new() {
            Field       = nameof(GetRequest.ReadMask).Underscore(),
            Description = $"The read_mask path `{path}` is invalid: {reason}.",
            Reason      = FieldReasons.InvalidReadMask,
        }]);
    }
}

/// <summary>
///     Trims single-resource responses to the fields named by the request's <c>read_mask</c>
///     per <seealso href="https://google.aip.dev/157">AIP-157: Partial responses</seealso>.
///     Runs only when the handler stashed a <see cref="ReadMaskRequested" /> marker — an omitted
///     or <c>*</c> mask never reaches this advisor.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TDetail">The detail DTO type.</typeparam>
public sealed class AdviceResponseReadMask<TEntity, TDetail> : IResourceResponseAdvisor<TEntity, TDetail>
    where TEntity : class, ICanonicalName
    where TDetail : class, ICanonicalName
{
    #region IResourceResponseAdvisor<TEntity,TDetail> Members

    public int Order => AdviceResponseReadMask.DefaultOrder;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        TEntity?          entity,
        TDetail?          detail,
        ClaimsPrincipal?  principal,
        CancellationToken ct = default
    ) {
        if (detail is null || !ctx.TryGet<ReadMaskRequested>(out var mask) || mask is null) {
            return Task.FromResult(AdviseResult.Continue);
        }

        AdviceResponseReadMask.Trim(detail, mask.Mask);

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}