using Schemata.Resource.Foundation.Filter.Terms;

namespace Schemata.Resource.Foundation.Filter.Values;

public class Integer : IValue
{
    public Integer(long value) {
        Value = value;
    }

    public long Value { get; }

    #region IValue Members

    public bool IsConstant => true;

    #endregion

    public override string ToString() {
        return Value.ToString();
    }
}
