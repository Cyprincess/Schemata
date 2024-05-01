using Parlot;

namespace Schemata.Resource.Foundation.Grammars.Operations;

public class SuffixMatch(TextPosition position) : Match
{
    public const string Name = "=$";

    public override TextPosition Position { get; } = position;

    public override string ToString() {
        return $"{Name}";
    }
}
