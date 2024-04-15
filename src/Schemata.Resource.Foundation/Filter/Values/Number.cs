using Schemata.Resource.Foundation.Filter.Terms;

namespace Schemata.Resource.Foundation.Filter.Values;

public class Number : IValue
{
    public Number(decimal value) {
        Value = value;
    }

    public decimal Value { get; }

    #region IValue Members

    public bool IsConstant => true;

    #endregion

    public override string ToString() {
        return Value.ToString("F");
    }
}
