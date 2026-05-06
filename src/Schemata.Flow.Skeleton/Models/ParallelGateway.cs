namespace Schemata.Flow.Skeleton.Models;

/// <summary>
///     A parallel (AND) gateway. On the split side (Fork), all outgoing branches
///     are activated unconditionally. On the join side, the gateway waits for
///     tokens on every incoming flow before producing one output token.
/// </summary>
public sealed class ParallelGateway : Gateway;
