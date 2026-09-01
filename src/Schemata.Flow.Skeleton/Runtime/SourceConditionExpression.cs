using System;
using System.Threading.Tasks;
using Schemata.Abstractions.Entities;

namespace Schemata.Flow.Skeleton.Runtime;

/// <summary>
///     Condition expression that evaluates a predicate against a source entity bound to the current token.
/// </summary>
/// <typeparam name="TSource">The source entity type.</typeparam>
public sealed class SourceConditionExpression<TSource> : IConditionExpression, ISourceCondition
    where TSource : class, ICanonicalName
{
    /// <summary>Initializes a source condition expression.</summary>
    /// <param name="name">The source binding name.</param>
    /// <param name="predicate">The predicate evaluated against the bound source.</param>
    public SourceConditionExpression(string name, Func<TSource, bool> predicate) {
        Name      = name;
        Predicate = predicate;
    }

    /// <summary>The source binding name.</summary>
    public string Name { get; }

    /// <summary>The bound source entity type.</summary>
    public Type SourceType => typeof(TSource);

    /// <summary>The predicate evaluated against the bound source.</summary>
    public Func<TSource, bool> Predicate { get; }

    #region IConditionExpression Members

    public async ValueTask<bool> Evaluate(FlowConditionContext context) {
        var task = context.CreateTaskContext();
        var source = await task.SourceAsync<TSource>(Name);
        return Predicate(source);
    }

    #endregion
}