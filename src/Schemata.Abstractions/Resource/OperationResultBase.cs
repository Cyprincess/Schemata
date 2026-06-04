namespace Schemata.Abstractions.Resource;

/// <summary>
///     CRTP base for operation results with a built-in allow/block gate.
/// </summary>
/// <typeparam name="TSelf">The concrete result type.</typeparam>
public abstract class OperationResultBase<TSelf>
    where TSelf : OperationResultBase<TSelf>, new()
{
    /// <summary>
    ///     The sentinel result returned when an operation is blocked by an advisor.
    /// </summary>
    public static readonly TSelf Blocked = new() { _allowed = false };

    private bool _allowed = true;

    /// <summary>
    ///     Returns <see langword="true" /> when the operation was not blocked and
    ///     the result payload passes type-specific validation.
    /// </summary>
    public bool IsAllowed() { return _allowed && IsValid(); }

    protected virtual bool IsValid() { return true; }
}
