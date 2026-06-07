using Schemata.Expressions.Skeleton;

namespace Schemata.Expressions.Cel.Expressions;

public abstract class CelNode : IExpressionTree
{
    #region IExpressionTree Members

    public string Language => CelLanguage.Name;

    #endregion
}