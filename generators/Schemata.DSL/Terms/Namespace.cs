using Parlot;
using static System.StringComparison;

namespace Schemata.DSL.Terms;

public class Namespace : TermBase
{
    public string Name { get; set; } = null!;

    // Namespace = "Namespace" WS QualifiedName
    public static Namespace? Parse(Mark mark, Scanner scanner) {
        if (!scanner.ReadText(nameof(Namespace), InvariantCultureIgnoreCase)) return null;

        scanner.SkipWhiteSpace();

        if (!ReadIdentifier(scanner, out var name)) {
            throw new ParseException("Expected a full-qualified name", scanner.Cursor.Position);
        }

        EnsureLineEnd(scanner);

        return new Namespace { Name = name.GetText() };
    }
}
