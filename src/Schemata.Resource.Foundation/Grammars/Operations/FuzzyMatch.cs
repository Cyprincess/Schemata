using Parlot;

namespace Schemata.Resource.Foundation.Grammars.Operations;

public class FuzzyMatch : Match
{
    public const string Name = "=~";

    public FuzzyMatch(TextPosition position) {
        Position = position;
    }

    public override TextPosition Position { get; }

    public override string ToString() {
        return $"{Name}";
    }
}
