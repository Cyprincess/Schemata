namespace Schemata.Resource.Foundation.Filter.Terms;

public class Term : ITerm
{
    public Term(string? unary, ISimple simple) {
        Modifier = unary;

        Simple = simple;
    }

    public string? Modifier { get; }

    public ISimple Simple { get; }

    #region ITerm Members

    public bool IsConstant => Simple.IsConstant;

    #endregion

    public override string? ToString() {
        return Modifier is not null ? $"{Modifier} {Simple}" : Simple.ToString();
    }
}
