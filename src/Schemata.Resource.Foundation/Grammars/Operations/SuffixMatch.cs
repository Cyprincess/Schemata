using Parlot;

namespace Schemata.Resource.Foundation.Grammars.Operations;

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
