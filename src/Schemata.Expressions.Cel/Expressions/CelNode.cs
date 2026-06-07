using Schemata.Expressions.Skeleton;

namespace Schemata.Expressions.Cel.Expressions;

/// <summary>Base AST node for the Common Expression Language compiler implementation.</summary>
public abstract class CelNode : IExpressionTree
{
    #region IExpressionTree Members

    public string Language => CelLanguage.Name;

    #endregion
}
