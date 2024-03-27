using System.Collections.Generic;
using Parlot;
using static System.StringComparison;

namespace Schemata.DSL.Terms;

public class Object : TermBase, INamedTerm
{
    public Note? Note { get; set; }

    public Dictionary<string, ObjectField>? Fields { get; set; }

    #region INamedTerm Members

    public string Name { get; set; } = null!;

    #endregion

    // Object = "Object" WS Name LC [ Note | ObjectField ] RC
    public static Object? Parse(Mark mark, Scanner scanner) {
        if (!scanner.ReadText(nameof(Object), InvariantCultureIgnoreCase)) {
            return null;
        }

        scanner.SkipWhiteSpace();

        if (!scanner.ReadIdentifier(out var name)) {
            throw new ParseException("Expected a name", scanner.Cursor.Position);
        }

        var @object = new Object { Name = name.GetText() };

        SkipWhiteSpaceOrCommentOrNewLine(scanner);

        if (scanner.ReadChar('{')) {
            while (true) {
                SkipWhiteSpaceOrCommentOrNewLine(scanner);
                if (scanner.ReadChar('}')) {
                    break;
                }

                var note = Note.Parse(mark, scanner);
                if (note is not null) {
                    @object.Note += note;
                    continue;
                }

                var field = ObjectField.Parse(mark, @object, scanner);
                if (field is not null) {
                    @object.Fields ??= new();
                    @object.Fields.Add(field.Name, field);
                    continue;
                }

                throw new ParseException($"Unexpected char {scanner.Cursor.Current}", scanner.Cursor.Position);
            }
        }

        return @object;
    }
}
