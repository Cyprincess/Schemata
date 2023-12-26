namespace Schemata.DSL.Terms;

public class Ref : TermBase, IValueTerm
{
    #region IValueTerm Members

    public string Body { get; set; } = null!;

    #endregion
}
