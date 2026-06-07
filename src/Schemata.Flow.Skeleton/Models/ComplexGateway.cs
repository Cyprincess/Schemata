using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Skeleton.Models;

/// <summary>
///     A complex gateway whose activation depends on a custom condition
///     defined by <see cref="ActivationCount" />.
/// </summary>
public sealed class ComplexGateway : Gateway
{
    /// <summary>
    ///     The condition expression that controls when this gateway activates.
    /// </summary>
    public IConditionExpression? ActivationCount { get; set; }
}
