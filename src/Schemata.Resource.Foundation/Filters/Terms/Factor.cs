using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Parlot;
using Schemata.Resource.Foundation.Filters.Operations;

namespace Schemata.Resource.Foundation.Filters.Terms;

public class Factor : Logical, IToken
{
    public Factor(TextPosition position, Term term, IReadOnlyCollection<Term>? terms) {
        Position = position;

        Terms.Add(term);

        if (terms is not null) {
            Terms.AddRange(terms);
        }
    }

    public List<Term> Terms { get; } = [];

    public override IEnumerable<IToken> Tokens => Terms;

    public override ExpressionType Operator => ExpressionType.OrElse;

    #region IToken Members

    public override TextPosition Position { get; }

    public override bool IsConstant => Terms.All(t => t.IsConstant);

    #endregion

    public override string? ToString() {
        return Terms.Count > 1 ? $"[OR {string.Join(' ', Terms)}]" : Terms.FirstOrDefault()?.ToString();
    }
}
