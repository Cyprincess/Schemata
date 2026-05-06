namespace Schemata.Flow.Skeleton.Models;

/// <summary>
///     An inclusive (OR) gateway. On the split side, every branch whose condition
///     evaluates to <c>true</c> is activated. On the merge side, the gateway
///     synchronizes by waiting for all activated-and-reachable branches
///     before producing one output token.
/// </summary>
public sealed class InclusiveGateway : Gateway
{ }
