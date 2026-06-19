using Parlot;

namespace Schemata.Expressions.Aip.Expressions;

/// <summary>
///     Represents an AIP simple expression with an optional unary modifier.
/// </summary>
public class Term : IToken
{
    /// <summary>
    ///     Creates a term from a simple expression and optional modifier.
    /// </summary>
    public Term(TextPosition position, string? unary, ISimple simple) {
        Modifier = unary;
        Simple   = simple;
        Position = position;
    }

    /// <summary>
    ///     Gets the unary modifier token applied to the term.
    /// </summary>
    public string? Modifier { get; }

    /// <summary>
    ///     Gets the simple expression wrapped by the term.
    /// </summary>
    public ISimple Simple { get; }

    #region IToken Members

    public TextPosition Position   { get; }
    public bool         IsConstant => Simple.IsConstant;

    #endregion

    public override string? ToString() { return Modifier is not null ? $"{Modifier} {Simple}" : Simple.ToString(); }
}
