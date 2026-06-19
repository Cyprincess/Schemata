using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Parlot;
using Schemata.Expressions.Aip.Operations;
using Schemata.Expressions.Skeleton;

namespace Schemata.Expressions.Aip.Expressions;

/// <summary>
///     Represents a complete AIP-160 filter expression.
/// </summary>
public class Filter : LogicalBase, IArg, ISimple, IExpressionTree
{
    /// <summary>
    ///     Creates a filter from the first sequence and additional AND sequences.
    /// </summary>
    public Filter(TextPosition position, Sequence sequence, IReadOnlyCollection<Sequence>? sequences) {
        Position = position;
        Sequences.Add(sequence);

        if (sequences is not null) {
            Sequences.AddRange(sequences);
        }
    }

    /// <summary>
    ///     Gets the sequences joined by this filter.
    /// </summary>
    public List<Sequence> Sequences { get; } = [];

    /// <summary>
    ///     The original filter source this tree was parsed from. Used as a lossless compile-cache key
    ///     instead of <see cref="ToString" />, whose display form does not round-trip quoting/escaping.
    /// </summary>
    public string Source { get; set; } = string.Empty;

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
