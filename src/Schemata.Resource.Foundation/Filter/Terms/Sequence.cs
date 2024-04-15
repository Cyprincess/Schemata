using System.Collections.Generic;
using System.Linq;

namespace Schemata.Resource.Foundation.Filter.Terms;

public class Sequence : ITerm
{
    public Sequence(IEnumerable<Factor> factors) {
        Factors.AddRange(factors);
    }

    public List<Factor> Factors { get; } = [];

    #region ITerm Members

    public bool IsConstant => Factors.All(f => f.IsConstant);

    #endregion

    public override string? ToString() {
        return Factors.Count > 1 ? $"{{{string.Join(' ', Factors)}}}" : Factors.FirstOrDefault()?.ToString();
    }
}
