using Schemata.Resource.Foundation.Filter.Terms;

namespace Schemata.Resource.Foundation.Filter.Operations;

public class GreaterThanOrEqual : IBinary
{
    public const string Name = ">=";

    #region IBinary Members

    public bool IsConstant => false;

    #endregion

    public override string ToString() {
        return $"{Name}";
    }
}
