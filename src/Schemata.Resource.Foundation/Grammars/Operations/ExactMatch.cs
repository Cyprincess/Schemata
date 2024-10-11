using Parlot;

namespace Schemata.Resource.Foundation.Grammars.Operations;

public class ExactMatch : Match
{
    public const string Name = "=@";

    public ExactMatch(TextPosition position) {
        Position = position;
    }

    public override TextPosition Position { get; }

    public override string ToString() {
        return $"{Name}";
    }
}
