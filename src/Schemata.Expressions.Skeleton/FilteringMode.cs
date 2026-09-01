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