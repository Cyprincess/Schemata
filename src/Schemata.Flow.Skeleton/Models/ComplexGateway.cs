using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Skeleton.Models;

/// <summary>
///     A complex gateway whose activation depends on a custom condition
///     defined by <see cref="ActivationCount" />.
/// </summary>
public sealed class ComplexGateway : Gateway
{
    private IConditionExpression? _activationCount;

    /// <summary>
    ///     The condition expression that controls when this gateway activates.
    /// </summary>
    public IConditionExpression? ActivationCount {
        get => _activationCount;
        set {
            EnsureMutable();
            _activationCount = value;
        }
    }

    /// <inheritdoc />
    protected internal override void FreezeCore() {
        base.FreezeCore();
        if (ActivationCount is FlowGraphNode node) {
            node.Freeze();
        }
    }
}
