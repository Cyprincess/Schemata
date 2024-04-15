using System.Collections.Generic;
using System.Linq;
using Parlot;

namespace Schemata.DSL.Terms;

public abstract class TermBase
{
    protected static string NormalizeType(Mark mark, INamedTerm? scope, string type) {
        if (type.Contains('.')) {
            return type;
        }

        if (Utilities.GetClrType(type) is not null) {
            return type;
        }

        if (scope is not null) {
            var scoped = $"{scope.Name}.{type}";

            type = scoped switch {
                var _ when mark.Enums?.ContainsKey(scoped) == true   => scoped,
                var _ when mark.Objects?.ContainsKey(scoped) == true => scoped,
                var _                                                => type,
            };
        }

        if (string.IsNullOrWhiteSpace(mark.Namespace?.Name)) {
            return type;
        }

        return $"{mark.Namespace?.Name}.{type}";
    }

    protected static bool ReadNamespacedIdentifier(Scanner scanner, out TokenResult result) {
        return scanner.ReadFirstThenOthers(static x => Character.IsIdentifierStart(x),
            static x => x == '.' || Character.IsIdentifierPart(x),
            out result);
    }

    protected static IEnumerable<Option> ParseOptions(Mark mark, Scanner scanner) {
        if (!scanner.ReadChar('[')) {
            yield break;
        }

        while (true) {
            SkipWhiteSpaceOrCommentOrNewLine(scanner);
            if (scanner.ReadChar(']')) {
                break;
            }

            var option = Option.Parse(mark, scanner);
            if (option is null) {
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
        if (!scanner.ReadWhile(x => !Character.IsNewLine(x), out var result)) {
            return;
        }

        if (allowBracket && result.Span[0] == '}') {
            scanner.Cursor.ResetPosition(position);
            return;
        }

        throw new ParseException($"Expected line break, ‘{result.GetText()}’ given", scanner.Cursor.Position);
    }
}
