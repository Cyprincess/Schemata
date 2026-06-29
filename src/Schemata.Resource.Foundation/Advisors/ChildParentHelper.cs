namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Shared helpers for the <c>IChild</c>-aware advisors. The derivation rule is
///     "strip the last collection segment and the last leaf placeholder"; any input
///     that cannot satisfy that rule produces <see langword="null" /> rather than a
///     half-built parent.
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
    public static string? DeriveParent(string? canonical) {
        if (string.IsNullOrWhiteSpace(canonical)) {
            return null;
        }

        var segments = canonical!.Split('/');
        if (segments.Length < 4 || segments.Length % 2 != 0) {
            return null;
        }

        return string.Join("/", segments, 0, segments.Length - 2);
    }
}
