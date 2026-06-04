using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Parlot;
using Schemata.Expressions.Aip.Operations;
using Schemata.Expressions.Skeleton;

namespace Schemata.Expressions.Aip.Expressions;

public class Filter : LogicalBase, IArg, ISimple, IExpressionTree
{
    public Filter(TextPosition position, Sequence sequence, IReadOnlyCollection<Sequence>? sequences) {
        Position = position;
        Sequences.Add(sequence);

        if (sequences is not null) {
            Sequences.AddRange(sequences);
        }
    }

    public List<Sequence> Sequences { get; } = [];

    public override IEnumerable<IToken> Tokens => Sequences;

    public override ExpressionType Operator => ExpressionType.AndAlso;

    #region IArg Members

    public override TextPosition Position { get; }

    public override bool IsConstant => Sequences.All(s => s.IsConstant);

    #endregion

    #region IExpressionTree Members

    public string Language => AipLanguage.Name;

    #endregion

    public override string? ToString() {
        return Sequences.Count > 1 ? $"[AND {string.Join(" ", Sequences)}]" : Sequences.FirstOrDefault()?.ToString();
    }
}
