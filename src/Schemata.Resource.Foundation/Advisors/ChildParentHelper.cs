using Schemata.Abstractions.Entities;
using Schemata.Common;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Shared helper for the <c>IChild</c>-aware advisors. Parent derivation is owned by
///     <see cref="ResourceNameDescriptor.ParentOf" />; a value without a collection/leaf tail
///     produces <see langword="null" />.
/// </summary>
internal static class ChildParentHelper
{
    /// <summary>
    ///     Derives the AIP-122 parent canonical from a child's full canonical name.
    ///     <c>tenants/t1/hosts/h1</c> yields <c>tenants/t1</c>; a root resource
    ///     (<c>tenants/t1</c>, two segments) yields <see langword="null" />.
    /// </summary>
    /// <param name="canonical">The child's full canonical name, or <see langword="null" />.</param>
    /// <returns>The parent canonical, or <see langword="null" /> when none can be derived.</returns>
    public static string? DeriveParent<TEntity>(string? canonical)
        where TEntity : class, ICanonicalName {
        return ResourceNameDescriptor.ForType<TEntity>().ParentOf(canonical);
    }
}
