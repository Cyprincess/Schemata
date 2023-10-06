using System.Collections.Generic;
using System.Linq;
using Parlot;
using static System.StringSplitOptions;

namespace Schemata.DSL.Terms;

public abstract class TermBase
{
    protected static bool ReadNamespacedIdentifier(Scanner scanner, out TokenResult result) {
        return scanner.ReadFirstThenOthers(static x => Character.IsIdentifierStart(x),
            static x => x == '.' || Character.IsIdentifierPart(x), out result);
    }

    protected static IEnumerable<Option> ParseOptions(Mark mark, Scanner scanner) {
        if (!scanner.ReadChar('[')) {
            yield break;
        }

        while (true) {
            SkipWhiteSpaceOrCommentOrNewLine(scanner);
            if (scanner.ReadChar(']')) break;

            var option = Option.Parse(mark, scanner);
            if (option == null) {
                throw new ParseException("Expected at least one option", scanner.Cursor.Position);
            }

            SkipWhiteSpaceOrCommentOrNewLine(scanner);
            scanner.ReadChar(',');

            yield return option;
        }
    }

    protected static void SkipComment(Scanner scanner) {
        if (scanner.ReadChar(';')) {
            scanner.ReadWhile(x => !Character.IsNewLine(x));
        }
    }

    protected static void SkipWhiteSpaceOrComment(Scanner scanner) {
        do {
            SkipComment(scanner);
        } while (scanner.SkipWhiteSpace());
    }

    protected static void SkipWhiteSpaceOrCommentOrNewLine(Scanner scanner) {
        do {
            SkipComment(scanner);
        } while (scanner.SkipWhiteSpaceOrNewLine());
    }

    protected static void EnsureLineEnd(Scanner scanner, bool allowBracket = false) {
        SkipWhiteSpaceOrComment(scanner);

        var position = scanner.Cursor.Position;
        if (!scanner.ReadWhile(x => !Character.IsNewLine(x), out var result)) return;

        if (allowBracket && result.Span[0] == '}') {
            scanner.Cursor.ResetPosition(position);
            return;
        }

        throw new ParseException($"Expected line break, ‘{result.GetText()}’ given", scanner.Cursor.Position);
    }

    protected static string ToCamelCase(string @string) {
        return @string.Split(new[] { "_", " " }, RemoveEmptyEntries)
                      .Select(s => char.ToUpperInvariant(s[0]) + s.Substring(1, s.Length - 1))
                      .Aggregate(string.Empty, (s1, s2) => s1 + s2);
    }
}
