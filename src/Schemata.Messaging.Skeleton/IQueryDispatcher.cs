namespace Schemata.Messaging.Skeleton;

/// <summary>Dispatches queries to their handlers, running the query advisor chain around each.</summary>
/// <remarks>
///     Carries no member of its own: dispatch is already fully expressed by
///     <see cref="IRequestDispatcher.SendAsync{TRequest,TResponse}" />, and a differently named
///     twin of it would add nothing a caller can express. The type exists so the read path is a
///     separate DI registration from <see cref="ICommandDispatcher" /> and can be replaced on its
///     own.
/// </remarks>
public interface IQueryDispatcher : IRequestDispatcher;
