namespace Schemata.Messaging.Skeleton;

/// <summary>A side-effect-free request that reads and produces a <typeparamref name="TResult" />.</summary>
/// <remarks>
///     Kept distinct from a plain <see cref="IRequest{TResponse}" /> so that the absence of side
///     effects is expressed in the type, and an advisor chain can act on it — routing to a read
///     replica, or serving from cache — without inspecting the payload.
/// </remarks>
/// <typeparam name="TResult">The result the query produces.</typeparam>
public interface IQuery<TResult> : IRequest<TResult>;
