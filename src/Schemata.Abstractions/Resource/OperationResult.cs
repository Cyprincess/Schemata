namespace Schemata.Abstractions.Resource;

/// <summary>
///     CRTP base for operation results with a built-in allow/block gate.
/// </summary>
/// <typeparam name="TSelf">The concrete result type.</typeparam>
public abstract class OperationResult<TSelf>
    where TSelf : OperationResult<TSelf>, new()
{
    public static readonly TSelf Blocked = new() { _allowed = false };

    private bool _allowed = true;

    /// <summary>
    ///     Returns <see langword="true" /> when the operation was not blocked and
    ///     the result payload passes type-specific validation.
    /// </summary>
    public bool IsAllowed() { return _allowed && IsValid(); }

    /// <summary>
    ///     Override to enforce type-specific validation on the result payload.
    /// </summary>
    protected virtual bool IsValid() { return true; }
}
