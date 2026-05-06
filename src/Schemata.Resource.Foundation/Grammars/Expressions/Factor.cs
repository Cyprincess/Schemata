using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Parlot;
using Schemata.Resource.Foundation.Grammars.Operations;

namespace Schemata.Resource.Foundation.Grammars.Expressions;

/// <summary>
///     Terms joined by explicit OR in the filter grammar.
/// </summary>
public class Factor : LogicalBase, IToken
{
    /// <summary>
    ///     Initializes a new factor with a mandatory term and optional additional terms.
    /// </summary>
    public Factor(TextPosition position, Term term, IReadOnlyCollection<Term>? terms) {
        Position = position;

        Terms.Add(term);

        if (terms is not null) {
            Terms.AddRange(terms);
        }
    }

    /// <summary>
    ///     Gets the terms combined with OR.
    /// </summary>
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
