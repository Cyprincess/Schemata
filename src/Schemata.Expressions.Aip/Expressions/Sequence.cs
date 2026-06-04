using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Parlot;
using Schemata.Expressions.Aip.Operations;

namespace Schemata.Expressions.Aip.Expressions;

public class Sequence : LogicalBase
{
    public Sequence(TextPosition position, IEnumerable<Factor> factors) {
        Position = position;
        Factors.AddRange(factors);
    }

    public List<Factor> Factors { get; } = [];

    public override IEnumerable<IToken> Tokens => Factors;

    public override ExpressionType Operator => ExpressionType.AndAlso;

    public override TextPosition Position   { get; }
    public override bool         IsConstant => Factors.All(f => f.IsConstant);

    public override string? ToString() {
        return Factors.Count > 1 ? $"{{{string.Join(" ", Factors)}}}" : Factors.FirstOrDefault()?.ToString();
    }
}
