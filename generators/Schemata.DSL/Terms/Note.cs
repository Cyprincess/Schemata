using Parlot;
using static System.StringComparison;

namespace Schemata.DSL.Terms;

public class Note : TermBase
{
    public string Comment { get; set; } = null!;

    // Note = "Note" WS Value
    public static Note? Parse(Mark mark, Scanner scanner) {
        if (!scanner.ReadText(nameof(Note), InvariantCultureIgnoreCase)) return null;

        SkipWhiteSpaceOrCommentOrNewLine(scanner);

        var value = Value.Parse(mark, scanner);
        if (value == null) {
            throw new ParseException("Expected a value", scanner.Cursor.Position);
        }

        EnsureLineEnd(scanner, true);

        return new Note { Comment = value.Body };
    }

    public static Note? operator +(Note? a, Note? b) {
        if (a == null) return b;
        if (b == null) return a;

        return new Note { Comment = string.Equals(a.Comment, b.Comment) ? a.Comment : $"{a.Comment}\n{b.Comment}" };
    }
}
