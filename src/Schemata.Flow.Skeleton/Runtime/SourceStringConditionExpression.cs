using System;
using System.Threading.Tasks;
using Schemata.Abstractions.Entities;

namespace Schemata.Flow.Skeleton.Runtime;

/// <summary>
///     Condition expression written in a string expression language, evaluated against a source
///     entity bound to the current token. The registry compiles the text when the process is
///     registered, using the compiler selected by the configuration's Language.
/// </summary>
/// <typeparam name="TSource">The source entity type.</typeparam>
public sealed class SourceStringConditionExpression<TSource>
    : IConditionExpression, ISourceCondition, IStringConditionExpression
    where TSource : class, ICanonicalName
{
    private Func<TSource, bool>? _predicate;

    /// <summary>Initializes a string condition expression.</summary>
    /// <param name="name">The source binding name.</param>
    /// <param name="expression">The expression source text.</param>
    public SourceStringConditionExpression(string name, string expression) {
        Name       = name;
        Expression = expression;
    }

    /// <summary>The source binding name.</summary>
    public string Name { get; }

    /// <summary>The bound source entity type.</summary>
    public Type SourceType => typeof(TSource);

    /// <summary>The expression source text.</summary>
    public string Expression { get; }

    #region IConditionExpression Members

    public async ValueTask<bool> Evaluate(FlowConditionContext context) {
        if (_predicate is null) {
            throw new InvalidOperationException(
                $"Condition expression '{Expression}' has not been compiled; register the process through IProcessRegistry.");
        }

        var task   = context.CreateTaskContext();
        var source = await task.SourceAsync<TSource>(Name);
        return _predicate(source);
    }

    #endregion

    #region IStringConditionExpression Members

    bool IStringConditionExpression.Compiled => _predicate is not null;

    void IStringConditionExpression.Bind(Delegate predicate) {
        _predicate = (Func<TSource, bool>)predicate;
    }

    #endregion
}
