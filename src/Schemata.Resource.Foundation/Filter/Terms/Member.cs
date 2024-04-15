using System.Collections.Generic;

namespace Schemata.Resource.Foundation.Filter.Terms;

public class Member : IComparable
{
    public Member(IValue value, IReadOnlyCollection<IField>? fields) {
        Value = value;

        if (fields is not null) {
            Fields.AddRange(fields);
        }
    }

    public IValue Value { get; }

    public List<IField> Fields { get; } = [];

    #region IComparable Members

    public bool IsConstant => Value.IsConstant && Fields.Count == 0;

    #endregion

    public override string? ToString() {
        return Fields.Count > 0 ? $"{Value}.{string.Join('.', Fields)}" : Value.ToString();
    }
}
