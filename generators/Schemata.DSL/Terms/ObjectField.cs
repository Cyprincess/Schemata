using System.Collections.Generic;
using Parlot;

namespace Schemata.DSL.Terms;

public class ObjectField : TermBase
{
    public string? Type { get; set; }

    public string Name { get; set; } = null!;

    public bool Nullable { get; set; }

    public Note? Note { get; set; }

    public ValueTermBase? Map { get; set; }

    public List<Option>? Options { get; set; }

    public Dictionary<string, ObjectField>? Fields { get; set; }

    // ObjectField = [Type WS] Name [ [WS] LB [ Option { [WS] , [WS] Option } ] RB ] [ [WS] LC [ Note, ObjectField ] RC ] [ [WS] EQ [WS] ( Value | Function ) ]
    public static ObjectField? Parse(Mark mark, Scanner scanner) {
        if (!ReadIdentifier(scanner, out var type)) return null;

        scanner.SkipWhiteSpace();

        var nullable = scanner.ReadChar('?');
        if (nullable) {
            scanner.SkipWhiteSpace();
        }

        var field = new ObjectField { Name = type.GetText(), Nullable = nullable };

        if (scanner.ReadIdentifier(out var name)) {
            field.Type = type.GetText();
            field.Name = name.GetText();
        }

        // TODO: check type

        SkipWhiteSpaceOrCommentOrNewLine(scanner);

        foreach (var option in ParseOptions(mark, scanner)) {
            switch (option.Name) {
                case Constants.Options.Omit:
                case Constants.Options.OmitAll:
                    break;
                default:
                    throw new ParseException($"Unexpected option {option.Name}", scanner.Cursor.Position);
            }

            field.Options ??= new List<Option>();
            field.Options.Add(option);
        }

        SkipWhiteSpaceOrCommentOrNewLine(scanner);

        if (scanner.ReadChar('{')) {
            while (true) {
                SkipWhiteSpaceOrCommentOrNewLine(scanner);
                if (scanner.ReadChar('}')) break;

                var note = Note.Parse(mark, scanner);
                if (note != null) {
                    field.Note += note;
                    continue;
                }

                var f = Parse(mark, scanner);
                if (f != null) {
                    field.Fields ??= new Dictionary<string, ObjectField>();
                    field.Fields.Add(f.Name, f);
                    continue;
                }

                throw new ParseException($"Unexpected char {scanner.Cursor.Current}", scanner.Cursor.Position);
            }
        }

        SkipWhiteSpaceOrCommentOrNewLine(scanner);

        if (scanner.ReadChar('=')) {
            SkipWhiteSpaceOrCommentOrNewLine(scanner);

            var function = Function.Parse(mark, scanner);
            if (function != null) {
                field.Map = function;
            } else if (ReadIdentifier(scanner, out var token)) {
                field.Map = new Ref { Body = token.GetText() };
            } else {
                field.Map = Value.Parse(mark, scanner);
            }
        }

        return field;
    }
}
