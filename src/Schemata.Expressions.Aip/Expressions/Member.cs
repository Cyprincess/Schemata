using System.Collections.Generic;
using Parlot;

namespace Schemata.Expressions.Aip.Expressions;

public class Member : IComparableArg
{
    public Member(TextPosition position, IValue value, IReadOnlyCollection<IField>? fields) {
        Position = position;
        Value    = value;

        if (fields is not null) {
            Fields.AddRange(fields);
        }
    }

    public IValue Value { get; }

    public List<IField> Fields { get; } = [];

    #region IComparableArg Members

    public TextPosition Position   { get; }
    public bool         IsConstant => Value.IsConstant && Fields.Count == 0;

    #endregion

    public override string? ToString() {
        return Fields.Count > 0 ? $"{Value}.{string.Join(".", Fields)}" : Value.ToString();
    }
}
