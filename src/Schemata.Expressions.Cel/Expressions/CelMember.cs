namespace Schemata.Expressions.Cel.Expressions;

/// <summary>
///     Represents a CEL member access expression.
/// </summary>
public sealed class CelMember : CelNode
{
    /// <summary>
    ///     Creates a member access for the target expression.
    /// </summary>
    public CelMember(CelNode target, string member) {
        Target = target;
        Member = member;
    }

    /// <summary>
    ///     Gets the expression that provides the member.
    /// </summary>
    public CelNode Target { get; }

    /// <summary>
    ///     Gets the accessed member name.
    /// </summary>
    public string Member { get; }
}
