using Schemata.Resource.Foundation.Filter.Terms;

namespace Schemata.Resource.Foundation.Filter.Values;

public class Text : IValue
{
    public Text(string value) {
        Value = value;
    }

    public string Value { get; }

    #region IValue Members

    public bool IsConstant => true;

    #endregion

    public override string ToString() {
        return $"\"{Value}\"";
    }
}
