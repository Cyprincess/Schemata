using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Parlot;
using Schemata.Resource.Foundation.Grammars.Operations;

namespace Schemata.Resource.Foundation.Grammars.Expressions;

/// <summary>
///     Top-level filter expression: sequences joined by explicit AND.
/// </summary>
public class Filter : Logical, IArg, ISimple
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

    /// <inheritdoc />
    public override IEnumerable<IToken> Tokens => Sequences;

    /// <inheritdoc />
    public override ExpressionType Operator => ExpressionType.AndAlso;

    #region IArg Members

    /// <inheritdoc />
    public override TextPosition Position { get; }

    /// <inheritdoc />
    public override bool IsConstant => Sequences.All(s => s.IsConstant);

    #endregion

    /// <inheritdoc />
    public override string? ToString() {
        return Sequences.Count > 1 ? $"[AND {string.Join(' ', Sequences)}]" : Sequences.FirstOrDefault()?.ToString();
    }
}
