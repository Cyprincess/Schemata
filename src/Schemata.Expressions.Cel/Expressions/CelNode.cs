using Schemata.Expressions.Skeleton;

namespace Schemata.Expressions.Cel.Expressions;

/// <summary>
///     Base AST node for Common Expression Language expressions.
/// </summary>
public abstract class CelNode : IExpressionTree
{
    /// <summary>
    ///     Gets or sets the original expression source. Compiler caching keys on the tree's
    ///     structural fingerprint instead, since this annotation is mutable.
    /// </summary>
    public string Source { get; set; } = string.Empty;

    #region IExpressionTree Members

    public string Language => CelLanguage.Name;

    #endregion
}
