namespace Schemata.Expressions.Skeleton;

/// <summary>
///     How a filter that the backend cannot fully translate is executed.
/// </summary>
public enum FilteringMode
{
    /// <summary>
    ///     Inherit: contributes no restriction when combined with other levels; an all-default
    ///     resolution falls back to <see cref="Strict" />.
    /// </summary>
    Default,

    /// <summary>
    ///     Compile and push the whole filter to the backend; an untranslatable filter fails at the
    ///     backend rather than running locally.
    /// </summary>
    Strict,

    /// <summary>
    ///     Push the translatable part and evaluate the remainder locally under a bounded scan.
    /// </summary>
    Residual,
}

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
