using System;
using System.Collections.Generic;
using Parlot;
using static System.StringComparison;

namespace Schemata.DSL.Terms;

public class Use : TermBase, INamedTerm
{
    public static ReadOnlySpan<char> Keyword => nameof(Use).AsSpan();

    #region INamedTerm Members

    public string Name { get; set; } = null!;

    #endregion

    // Use = "Use" WS QualifiedName { [WS] , [WS] QualifiedName }
    public static IEnumerable<Use>? Parse(Mark mark, Scanner scanner) {
        if (!scanner.ReadText(Keyword, InvariantCultureIgnoreCase)) {
            return null;
        }

        return Parse(scanner);
    }

    private static IEnumerable<Use> Parse(Scanner scanner) {
        while (true) {
            scanner.SkipWhiteSpace();

            if (!ReadNamespacedIdentifier(scanner, out var name)) {
                throw new ParseException("Expected a full-qualified name", scanner.Cursor.Position);
            }

            yield return new() { Name = name.ToString() };

            scanner.SkipWhiteSpace();

            if (!scanner.ReadChar(',')) {
                break;
            }
        }

        EnsureLineEnd(scanner);
    }
}
