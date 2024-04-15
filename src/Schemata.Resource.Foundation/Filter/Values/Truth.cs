using Schemata.Resource.Foundation.Filter.Terms;

namespace Schemata.Resource.Foundation.Filter.Values;

public class Truth : IValue
{
    public Truth(bool value) {
        Value = value;
    }

    public bool Value { get; }

    #region IValue Members

    public bool IsConstant => true;

    #endregion

    public override string ToString() {
        return Value ? "\u2611" : "\u2612";
    }
}
