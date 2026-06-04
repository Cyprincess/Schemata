using Parlot;

namespace Schemata.Expressions.Aip.Expressions;

public class Term : IToken
{
    public Term(TextPosition position, string? unary, ISimple simple) {
        Modifier = unary;
        Simple   = simple;
        Position = position;
    }

    public string? Modifier { get; }

    public ISimple Simple { get; }

    #region IToken Members

    public TextPosition Position   { get; }
    public bool         IsConstant => Simple.IsConstant;

    #endregion

    public override string? ToString() { return Modifier is not null ? $"{Modifier} {Simple}" : Simple.ToString(); }
}
