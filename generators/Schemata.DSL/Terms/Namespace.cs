using Parlot;
using static System.StringComparison;

namespace Schemata.DSL.Terms;

public class Namespace : TermBase, INamedTerm
{
    #region INamedTerm Members

    public string Name { get; set; } = null!;

    #endregion

    // Namespace = "Namespace" WS QualifiedName
    public static Namespace? Parse(Mark mark, Scanner scanner) {
        if (!scanner.ReadText(nameof(Namespace), InvariantCultureIgnoreCase)) {
            return null;
        }

        scanner.SkipWhiteSpace();

        if (!ReadNamespacedIdentifier(scanner, out var name)) {
            throw new ParseException("Expected a full-qualified name", scanner.Cursor.Position);
        }

        EnsureLineEnd(scanner);

        return new() { Name = name.GetText() };
    }
}
