using Schemata.Resource.Foundation.Filter.Terms;

namespace Schemata.Resource.Foundation.Filter.Values;

public class Null : IValue
{
    public object? Value { get; } = null;

    #region IValue Members

    public bool IsConstant => true;

    #endregion

    public override string ToString() {
        return "\u2205";
    }
}
