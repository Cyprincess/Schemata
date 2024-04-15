using System.Collections.Generic;
using System.Linq;

namespace Schemata.Resource.Foundation.Filter.Terms;

public class Factor : ITerm
{
    public Factor(Term term, IReadOnlyCollection<Term>? terms) {
        Terms.Add(term);

        if (terms is not null) {
            Terms.AddRange(terms);
        }
    }

    public List<Term> Terms { get; } = [];

    #region ITerm Members

    public bool IsConstant => Terms.All(t => t.IsConstant);

    #endregion

    public override string? ToString() {
        return Terms.Count > 1 ? $"[OR {string.Join(' ', Terms)}]" : Terms.FirstOrDefault()?.ToString();
    }
}
