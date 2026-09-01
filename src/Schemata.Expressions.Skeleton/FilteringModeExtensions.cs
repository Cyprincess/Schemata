namespace Schemata.Expressions.Skeleton;

/// <summary>
///     Combines filtering modes configured at different levels.
/// </summary>
public static class FilteringModeExtensions
{
    /// <summary>
    ///     Combines two modes by intersection: the result is the more restrictive of the two, so a
    ///     <see cref="FilteringMode.Strict" /> at any level wins and <see cref="FilteringMode.Default" />
    ///     yields to the other. Combining narrows capability and never widens it.
    /// </summary>
    public static FilteringMode Narrow(this FilteringMode left, FilteringMode right) {
        if (left is FilteringMode.Strict || right is FilteringMode.Strict) {
            return FilteringMode.Strict;
        }

        if (left is FilteringMode.Residual || right is FilteringMode.Residual) {
            return FilteringMode.Residual;
        }

        return FilteringMode.Default;
    }

    /// <summary>
    ///     Resolves an inherited mode to a concrete one, defaulting to <see cref="FilteringMode.Strict" />.
    /// </summary>
    public static FilteringMode OrStrict(this FilteringMode mode) {
        return mode is FilteringMode.Default ? FilteringMode.Strict : mode;
    }
}