using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Parlot;
using Schemata.Resource.Foundation.Grammars.Operations;

namespace Schemata.Resource.Foundation.Grammars.Expressions;

/// <summary>
///     Top-level filter expression: sequences joined by explicit AND.
/// </summary>
public class Filter : LogicalBase, IArg, ISimple
{
    /// <summary>
    ///     Initializes a new filter with a mandatory sequence and optional additional sequences.
    /// </summary>
    public Filter(TextPosition position, Sequence sequence, IReadOnlyCollection<Sequence>? sequences) {
        Position = position;

        Sequences.Add(sequence);

        if (sequences is not null) {
            Sequences.AddRange(sequences);
        }
    }

    /// <summary>
    ///     Gets the sequences combined with AND.
    /// </summary>
    public List<Sequence> Sequences { get; } = [];

    public override IEnumerable<IToken> Tokens => Sequences;

    public override ExpressionType Operator => ExpressionType.AndAlso;

    #region IArg Members

    public override TextPosition Position { get; }

    public override bool IsConstant => Sequences.All(s => s.IsConstant);

    #endregion

    public override string? ToString() {
        return Sequences.Count > 1 ? $"[AND {string.Join(' ', Sequences)}]" : Sequences.FirstOrDefault()?.ToString();
    }
}
