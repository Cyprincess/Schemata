using System;
using System.Text;
using Parlot;

namespace Schemata.DSL.Terms;

public class Value : TermBase, IValueTerm
{
    public Type Type { get; set; } = null!;

    #region IValueTerm Members

    public string Body { get; set; } = null!;

    #endregion

    // Value = String | QuotedString | Number | Boolean | MultilineString | Null
    public static Value? Parse(Mark mark, Scanner scanner) {
        return true switch {
            _ when ReadMultilineString(scanner, out var multi) => new Value {
                Body = multi, //
                Type = typeof(string),
            },
            _ when scanner.ReadQuotedString(out var quoted) => new Value {
                Body = quoted.Span[1..^1].ToString(), //
                Type = typeof(string),
            },
            _ when scanner.ReadDecimal(out var number) => new Value {
                Body = number.GetText(), //
                Type = number.GetText().IndexOf('.') >= 0 ? typeof(decimal) : typeof(long),
            },
            _ when ReadSingleLineString(scanner, out var simple) => new Value {
                Body = simple, //
                Type = typeof(object),
            },
            _ => null,
        };
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

    private static bool ReadSingleLineString(Scanner scanner, out string @string) {
        if (scanner.ReadWhile(x => !Character.IsWhiteSpaceOrNewLine(x) && !x.IsStopWord(), out var result)) {
            @string = result.GetText();
            return true;
        }

        @string = string.Empty;
        return false;
    }
}
