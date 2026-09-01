namespace Schemata.Messaging.Skeleton;

/// <summary>Handles a <typeparamref name="TQuery" /> and produces a <typeparamref name="TResult" />.</summary>
/// <typeparam name="TQuery">The query type this handler answers.</typeparam>
/// <typeparam name="TResult">The result type.</typeparam>
public interface IQueryHandler<in TQuery, TResult> : IRequestHandler<TQuery, TResult>
    where TQuery : IQuery<TResult>;
