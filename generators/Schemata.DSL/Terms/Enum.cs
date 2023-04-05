using System.Collections.Generic;
using Parlot;
using static System.StringComparison;

namespace Schemata.DSL.Terms;

public class Enum : TermBase
{
    public string Name { get; set; } = null!;

    public Note? Note { get; set; }

    public Dictionary<string, EnumValue>? Values { get; set; }

    // Enum = "Enum" WS Name [WS] LC [EnumValue | Note] RC
    public static Enum? Parse(Mark mark, Scanner scanner) {
        if (!scanner.ReadText(nameof(Enum), InvariantCultureIgnoreCase)) return null;

        scanner.SkipWhiteSpace();

        if (!scanner.ReadIdentifier(out var name)) {
            throw new ParseException("Expected a name", scanner.Cursor.Position);
        }

        SkipWhiteSpaceOrCommentOrNewLine(scanner);

        if (!scanner.ReadChar('{')) {
            throw new ParseException($"Expected start of square bracket, ‘{scanner.Cursor.Current}’ given",
                scanner.Cursor.Position);
        }

        var @enum = new Enum { Name = name.GetText() };

        while (true) {
            SkipWhiteSpaceOrCommentOrNewLine(scanner);
            if (scanner.ReadChar('}')) break;

            var note = Note.Parse(mark, scanner);
            if (note != null) {
                @enum.Note += note;
                continue;
            }

            var value = EnumValue.Parse(mark, scanner);
            if (value != null) {
                @enum.Values ??= new Dictionary<string, EnumValue>();
                if (@enum.Values.ContainsKey(value.Name)) {
                    throw new ParseException($"Duplicate value {value.Name}", scanner.Cursor.Position);
                }

                @enum.Values.Add(value.Name, value);
                continue;
            }

            throw new ParseException($"Unexpected char {scanner.Cursor.Current}", scanner.Cursor.Position);
        }

        if (@enum.Values?.Count <= 0) {
            throw new ParseException("Expected at least one enum value", scanner.Cursor.Position);
        }

        return @enum;
    }
}
