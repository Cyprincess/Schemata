using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Parlot;
using Schemata.Expressions.Aip.Operations;

namespace Schemata.Expressions.Aip.Expressions;

/// <summary>
///     Represents one or more AIP terms joined by OR.
/// </summary>
public class Factor : LogicalBase
{
    /// <summary>
    ///     Creates an OR factor from the first term and additional terms.
    /// </summary>
    public Factor(TextPosition position, Term term, IReadOnlyCollection<Term>? terms) {
        Position = position;
        Terms.Add(term);

        if (terms is not null) {
            Terms.AddRange(terms);
        }
    }

    /// <summary>
    ///     Gets the terms joined by this factor.
    /// </summary>
    public List<Term> Terms { get; } = [];

    public override IEnumerable<IToken> Tokens => Terms;

    public override ExpressionType Operator => ExpressionType.OrElse;

    public override TextPosition Position   { get; }
    public override bool         IsConstant => Terms.All(t => t.IsConstant);

    public override string? ToString() {
        return Terms.Count > 1 ? $"[OR {string.Join(" ", Terms)}]" : Terms.FirstOrDefault()?.ToString();
    }
}
