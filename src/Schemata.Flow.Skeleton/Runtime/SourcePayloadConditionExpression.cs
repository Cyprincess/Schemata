using System;
using System.Threading.Tasks;
using Schemata.Abstractions.Entities;

namespace Schemata.Flow.Skeleton.Runtime;

/// <summary>
///     Condition expression that evaluates a predicate against a source entity and typed event payload.
/// </summary>
/// <typeparam name="TSource">The source entity type.</typeparam>
/// <typeparam name="TPayload">The payload type.</typeparam>
public sealed class SourcePayloadConditionExpression<TSource, TPayload> : IConditionExpression, ISourceCondition
    where TSource : class, ICanonicalName
{
    /// <summary>Initializes a source and payload condition expression.</summary>
    /// <param name="name">The source binding name.</param>
    /// <param name="predicate">The predicate evaluated against the bound source and payload.</param>
    public SourcePayloadConditionExpression(string name, Func<TSource, TPayload, bool> predicate) {
        Name      = name;
        Predicate = predicate;
    }

    /// <summary>The source binding name.</summary>
    public string Name { get; }

    /// <summary>The bound source entity type.</summary>
    public Type SourceType => typeof(TSource);

    /// <summary>The predicate evaluated against the bound source and payload.</summary>
    public Func<TSource, TPayload, bool> Predicate { get; }

    #region IConditionExpression Members

    public async ValueTask<bool> Evaluate(FlowConditionContext context) {
        if (context.Payload is not TPayload payload) {
            return false;
        }

        var task   = context.CreateTaskContext();
        var source = await task.SourceAsync<TSource>(Name);
        return Predicate(source, payload);
    }

    #endregion
}