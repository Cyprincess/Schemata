namespace Schemata.Abstractions.Resource;

/// <summary>
///     Base class for operation results that track whether the operation was allowed by the advisor pipeline.
/// </summary>
/// <typeparam name="TSelf">The concrete result type (CRTP pattern).</typeparam>
public abstract class OperationResult<TSelf>
    where TSelf : OperationResult<TSelf>, new()
{
    /// <summary>
    ///     A pre-built result representing a blocked operation.
    /// </summary>
    public static readonly TSelf Blocked = new() { _allowed = false };

    private bool _allowed = true;

    /// <summary>
    ///     Returns <see langword="true" /> if the operation was allowed by the pipeline and the result is valid.
    /// </summary>
    /// <returns><see langword="true" /> if the operation should proceed.</returns>
    public bool IsAllowed() { return _allowed && IsValid(); }

    /// <summary>
    ///     Returns <see langword="true" /> if the result contains valid data.
    /// </summary>
    /// <returns><see langword="true" /> by default; override to add validation.</returns>
    protected virtual bool IsValid() { return true; }
}
