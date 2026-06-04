using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Parlot;
using Schemata.Expressions.Aip.Operations;

namespace Schemata.Expressions.Aip.Expressions;

public class Factor : LogicalBase
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

    public override TextPosition Position   { get; }
    public override bool         IsConstant => Terms.All(t => t.IsConstant);

    public override string? ToString() {
        return Terms.Count > 1 ? $"[OR {string.Join(" ", Terms)}]" : Terms.FirstOrDefault()?.ToString();
    }
}
