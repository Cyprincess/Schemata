using Schemata.Abstractions;

namespace Schemata.Messaging.Skeleton;

/// <summary>A state-changing request that produces no result.</summary>
/// <remarks>
///     Reuses <see cref="Unit" /> as the response so a body-less command still flows through the one
///     <see cref="IRequestDispatcher" /> contract instead of needing a parallel dispatch path.
/// </remarks>
public interface ICommand : IRequest<Unit>;

/// <summary>A state-changing request that produces a <typeparamref name="TResult" />.</summary>
/// <typeparam name="TResult">The result the command produces.</typeparam>
public interface ICommand<TResult> : IRequest<TResult>;
