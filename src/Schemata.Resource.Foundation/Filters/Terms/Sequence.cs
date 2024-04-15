using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Parlot;
using Schemata.Resource.Foundation.Filters.Operations;

namespace Schemata.Resource.Foundation.Filters.Terms;

public class Sequence : Logical, IToken
{
    public Sequence(TextPosition position, IEnumerable<Factor> factors) {
        Position = position;

        Factors.AddRange(factors);
    }

    public List<Factor> Factors { get; } = [];

    public override IEnumerable<IToken> Tokens => Factors;

    public override ExpressionType Operator => ExpressionType.AndAlso;

    #region IToken Members

    public override TextPosition Position { get; }

    public override bool IsConstant => Factors.All(f => f.IsConstant);

    #endregion

    public override string? ToString() {
        return Factors.Count > 1 ? $"{{{string.Join(' ', Factors)}}}" : Factors.FirstOrDefault()?.ToString();
    }
}
