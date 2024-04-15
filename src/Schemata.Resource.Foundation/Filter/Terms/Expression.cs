using System.Collections.Generic;
using System.Linq;

namespace Schemata.Resource.Foundation.Filter.Terms;

public class Expression : IArg, ISimple
{
    public Expression(Sequence sequence, IReadOnlyCollection<Sequence>? sequences) {
        Sequences.Add(sequence);

        if (sequences is not null) {
            Sequences.AddRange(sequences);
        }
    }

    public List<Sequence> Sequences { get; } = [];

    #region IArg Members

    public bool IsConstant => Sequences.All(s => s.IsConstant);

    #endregion

    public override string? ToString() {
        return Sequences.Count > 1 ? $"[AND {string.Join(' ', Sequences)}]" : Sequences.FirstOrDefault()?.ToString();
    }
}
