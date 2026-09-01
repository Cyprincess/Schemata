using Schemata.Messaging.Skeleton;

namespace Schemata.Insight.Skeleton;

/// <summary>Marks an Insight read query that produces <typeparamref name="TResult" />.</summary>
/// <typeparam name="TResult">The query result type.</typeparam>
public interface IInsightQuery<TResult> : IQuery<TResult>;
