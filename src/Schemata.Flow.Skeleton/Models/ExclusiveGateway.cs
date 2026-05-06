namespace Schemata.Flow.Skeleton.Models;

/// <summary>
///     An exclusive (XOR) gateway that routes the token to exactly one outgoing branch.
///     Conditions are evaluated in order; the first true branch wins.
///     The merge side is a pass-through — each arriving token passes independently.
/// </summary>
public sealed class ExclusiveGateway : Gateway
{ }
