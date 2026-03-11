namespace Schemata.Abstractions.Resource;

public abstract class OperationResult<TSelf>
    where TSelf : OperationResult<TSelf>, new()
{
    public static readonly TSelf Blocked = new() { _allowed = false };

    private bool _allowed = true;

    public bool IsAllowed() { return _allowed && IsValid(); }

    protected virtual bool IsValid() { return true; }
}
