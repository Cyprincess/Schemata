using Schemata.Expressions.Skeleton;

namespace Schemata.Expressions.Cel.Expressions;

/// <summary>Base AST node for the Common Expression Language compiler implementation.</summary>
public abstract class CelNode : IExpressionTree
{
    /// <summary>
    ///     The original expression source this tree was parsed from. Used as a lossless compile-cache
    ///     key so distinct expressions never share a compiled delegate.
    /// </summary>
    public string Source { get; set; } = string.Empty;

    #region IExpressionTree Members

    public string Language => CelLanguage.Name;

    #endregion
}
