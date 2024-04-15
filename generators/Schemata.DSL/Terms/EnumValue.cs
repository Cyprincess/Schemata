using Parlot;

namespace Schemata.DSL.Terms;

public class EnumValue : TermBase, INamedTerm
{
    public string Body { get; set; } = null!;

    public Note? Note { get; set; }

    #region INamedTerm Members

    public string Name { get; set; } = null!;

    #endregion

    // EnumValue = Name [ [WS] EQ [WS] Value ] [ [WS] LC [Note] RC ]
    public static EnumValue? Parse(Mark mark, Scanner scanner) {
        if (!scanner.ReadIdentifier(out var name)) {
            throw new ParseException("Expected a name", scanner.Cursor.Position);
        }

        SkipWhiteSpaceOrCommentOrNewLine(scanner);

        var identifier = name.GetText();

        string? body = null;
        if (scanner.ReadChar('=')) {
            SkipWhiteSpaceOrCommentOrNewLine(scanner);
            var value = Value.Parse(mark, scanner);
            if (value is null) {
                throw new ParseException("Expected a value", scanner.Cursor.Position);
            }

            body = value.Body;
        }

        var @enum = new EnumValue {
            Name = identifier,
            Body = body ?? identifier,
        };

        SkipWhiteSpaceOrCommentOrNewLine(scanner);

        if (scanner.ReadChar('{')) {
            while (true) {
                SkipWhiteSpaceOrCommentOrNewLine(scanner);
                if (scanner.ReadChar('}')) {
                    break;
                }

                var property = Property.Parse(mark, scanner);
                if (property?.Name == nameof(Note)) {
                    @enum.Note += new Note { Comment = property.Body };
                    continue;
                }

                if (property is not null) {
                    throw new ParseException($"Invalid property {property.Name}", scanner.Cursor.Position);
                }

                throw new ParseException($"Unexpected char {scanner.Cursor.Current}", scanner.Cursor.Position);
            }
        }

        return @enum;
    }
}
