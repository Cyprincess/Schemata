using System.Collections.Generic;
using Parlot;

namespace Schemata.DSL.Terms;

public class ObjectField : TermBase, INamedTerm
{
    public string? Type { get; set; }

    public bool Nullable { get; set; }

    public Note? Note { get; set; }

    public IValueTerm? Map { get; set; }

    public List<Option>? Options { get; set; }

    public Dictionary<string, ObjectField>? Fields { get; set; }

    #region INamedTerm Members

    public string Name { get; set; } = null!;

    #endregion

    // ObjectField = [Type WS] Name [ [WS] LB [ Option { [WS] , [WS] Option } ] RB ] [ [WS] LC [ Note | ObjectField ] RC ] [ [WS] EQ [WS] ( Value | Function | Ref ) ]
    public static ObjectField? Parse(Mark mark, Object? @object, Scanner scanner) {
        if (!ReadNamespacedIdentifier(scanner, out var type)) {
            return null;
        }

        scanner.SkipWhiteSpace();

        var nullable = scanner.ReadChar('?');
        if (nullable) {
            scanner.SkipWhiteSpace();
        }

        var field = new ObjectField {
            Name     = type.GetText(),
            Nullable = nullable,
        };

        if (scanner.ReadIdentifier(out var name)) {
            field.Type = NormalizeType(mark, @object, type.GetText());
            field.Name = name.GetText();
        }

        SkipWhiteSpaceOrCommentOrNewLine(scanner);

        foreach (var option in ParseOptions(mark, scanner)) {
            switch (option.Name) {
                case SkmConstants.Options.Omit:
                case SkmConstants.Options.OmitAll:
                    break;
                default:
                    throw new ParseException($"Unexpected option {option.Name}", scanner.Cursor.Position);
            }

            field.Options ??= [];
            field.Options.Add(option);
        }

        SkipWhiteSpaceOrCommentOrNewLine(scanner);

        if (scanner.ReadChar('{')) {
            while (true) {
                SkipWhiteSpaceOrCommentOrNewLine(scanner);
                if (scanner.ReadChar('}')) {
                    break;
                }

                var note = Note.Parse(mark, scanner);
                if (note is not null) {
                    field.Note += note;
                    continue;
                }

                var nested = field.Type is not null ? mark.Objects?[field.Type] : null;
                var f      = Parse(mark, nested, scanner);
                if (f is not null) {
                    field.Fields ??= new();
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
            if (function is not null) {
                field.Map = function;
            } else if (ReadNamespacedIdentifier(scanner, out var token)) {
                field.Map = new Ref { Body = token.GetText() };
            } else {
                field.Map = Value.Parse(mark, scanner);
            }

            if (field.Map is null) {
                throw new ParseException("Expected an expression", scanner.Cursor.Position);
            }
        }

        return field;
    }
}
