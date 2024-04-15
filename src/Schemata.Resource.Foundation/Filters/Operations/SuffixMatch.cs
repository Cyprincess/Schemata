using Parlot;

namespace Schemata.Resource.Foundation.Filters.Operations;

public class SuffixMatch : Match
{
    public const string Name = "=$";

    public SuffixMatch(TextPosition position) {
        Position = position;
    }

    public override TextPosition Position { get; }

    public override string ToString() {
        return $"{Name}";
    }
}
