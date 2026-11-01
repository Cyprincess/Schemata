namespace Schemata.Messaging.Skeleton.Runtime;

/// <summary>
///     Registration contract stating that the in-process dispatcher owns the host's public
///     <see cref="IRequestDispatcher" />, <see cref="ICommandDispatcher" /> and
///     <see cref="IQueryDispatcher" /> slots. It is registered only when the in-process
///     registrations win those slots; a transport registered earlier keeps them and the contract
///     stays absent.
/// </summary>
public sealed class InProcessDispatcherOwnership
{
}
