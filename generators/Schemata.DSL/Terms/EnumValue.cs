using Parlot;

namespace Schemata.DSL.Terms;

public class EnumValue : TermBase
{
    public string Name { get; set; } = null!;

    public string? Body { get; set; }

    public Note? Note { get; set; }

    // EnumValue = Name [ [WS] = [WS] Value ] [ [WS] LC [Note] RC ]
    public static EnumValue? Parse(Mark mark, Scanner scanner) {
        if (!scanner.ReadIdentifier(out var name)) {
            throw new ParseException("Expected a name", scanner.Cursor.Position);
        }

        var @enum = new EnumValue { Name = name.GetText() };

        scanner.SkipWhiteSpace();

        if (scanner.ReadChar('=')) {
            var value = Value.Parse(mark, scanner);
            @enum.Body = value.Body;
        }

        SkipWhiteSpaceOrCommentOrNewLine(scanner);

        if (scanner.ReadChar('{')) {
            while (true) {
                SkipWhiteSpaceOrCommentOrNewLine(scanner);
                if (scanner.ReadChar('}')) break;

                var property = Property.Parse(mark, scanner);
                if (property?.Name == nameof(Note)) {
                    @enum.Note += new Note { Comment = property.Body };
                    continue;
                }

                if (property != null) {
                    throw new ParseException($"Invalid property {property.Name}", scanner.Cursor.Position);
                }

                throw new ParseException($"Unexpected char {scanner.Cursor.Current}", scanner.Cursor.Position);
            }
        }

        return @enum;
    }
}
