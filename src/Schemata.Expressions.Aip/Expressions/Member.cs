using System.Collections.Generic;
using Parlot;

namespace Schemata.Expressions.Aip.Expressions;

/// <summary>
///     Represents an AIP value followed by member path segments.
/// </summary>
public class Member : IComparableArg
{
    /// <summary>
    ///     Creates a member path from a root value and field segments.
    /// </summary>
    public Member(TextPosition position, IValue value, IReadOnlyCollection<IField>? fields) {
        Position = position;
        Value    = value;

        if (fields is not null) {
            Fields.AddRange(fields);
        }
    }

    /// <summary>
    ///     Gets the root value of the member path.
    /// </summary>
    public IValue Value { get; }

    /// <summary>
    ///     Gets the field segments after the root value.
    /// </summary>
    public List<IField> Fields { get; } = [];

    #region IComparableArg Members

    public TextPosition Position   { get; }
    public bool         IsConstant => Value.IsConstant && Fields.Count == 0;

    #endregion

    public override string? ToString() {
        return Fields.Count > 0 ? $"{Value}.{string.Join(".", Fields)}" : Value.ToString();
    }
}
