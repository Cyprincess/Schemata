using Parlot;

namespace Schemata.Resource.Foundation.Filters.Operations;

public class PrefixMatch : Match
{
    public const string Name = "=^";

    public PrefixMatch(TextPosition position) {
        Position = position;
    }

    public override TextPosition Position { get; }

    public override string ToString() {
        return $"{Name}";
    }
}
