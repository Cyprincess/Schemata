using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Parlot;
using Schemata.Resource.Foundation.Grammars.Operations;

namespace Schemata.Resource.Foundation.Grammars.Expressions;

/// <summary>
///     Factors implicitly joined by whitespace (logical AND) in the filter grammar.
/// </summary>
public class Sequence : Logical, IToken
{
    /// <summary>
    ///     Initializes a new sequence from a collection of factors.
    /// </summary>
    public Sequence(TextPosition position, IEnumerable<Factor> factors) {
        Position = position;

        Factors.AddRange(factors);
    }

    /// <summary>
    ///     Gets the factors combined with implicit AND.
    /// </summary>
    public List<Factor> Factors { get; } = [];

    /// <inheritdoc />
    public override IEnumerable<IToken> Tokens => Factors;

    /// <inheritdoc />
    public override ExpressionType Operator => ExpressionType.AndAlso;

    #region IToken Members

    /// <inheritdoc />
    public override TextPosition Position { get; }

    /// <inheritdoc />
    public override bool IsConstant => Factors.All(f => f.IsConstant);

    #endregion

    /// <inheritdoc />
    public override string? ToString() {
        return Factors.Count > 1 ? $"{{{string.Join(' ', Factors)}}}" : Factors.FirstOrDefault()?.ToString();
    }
}
