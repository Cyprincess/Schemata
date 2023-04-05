using System.Text;
using Parlot;

namespace Schemata.DSL.Terms;

public class Value : ValueTermBase
{
    // Value = String | QuotedString | Number | Boolean | MultilineString | Null
    public static Value Parse(Mark mark, Scanner scanner) {
        var value = true switch {
            _ when ReadMultilineString(scanner, out var multi) => new Value { Body = multi },
            _ when scanner.ReadQuotedString(out var quoted) => new Value { Body = quoted.Span[1..^1].ToString() },
            _ when scanner.ReadNonWhiteSpaceOrNewLine(out var simple) => new Value { Body = simple.GetText() },
            _ => throw new ParseException("Expected a value", scanner.Cursor.Position),
        };

        return value;
    }

    private static bool ReadMultilineString(Scanner scanner, out string @string) {
        var multi = scanner.ReadText("'''");
        if (multi) {
            var sb = new StringBuilder();
            while (true) {
                if (scanner.Cursor.Match("'''")) {
                    scanner.Cursor.Advance(3);
                    @string = sb.ToString();
                    return true;
                }

                sb.Append(scanner.Cursor.Current);
                scanner.Cursor.Advance();
            }
        }

        @string = string.Empty;
        return false;
    }
}
