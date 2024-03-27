using System.Collections.Generic;
using Parlot;

namespace Schemata.DSL.Terms;

public class Mark : TermBase
{
    public int Length { get; set; }

    public Namespace? Namespace { get; set; }

    public Dictionary<string, Enum>? Enums { get; set; }

    public Dictionary<string, Object>? Objects { get; set; }

    public Dictionary<string, Entity>? Tables { get; set; }

    public Dictionary<string, Trait>? Traits { get; set; }

    // Mark = {Namespace | Enum | Entity | Trait}
    public static Mark? Parse(Scanner scanner) {
        var mark = new Mark();

        while (true) {
            SkipWhiteSpaceOrCommentOrNewLine(scanner);
            if (scanner.Cursor.Eof) {
                break;
            }

            var @namespace = Namespace.Parse(mark, scanner);
            if (@namespace is not null) {
                if (mark.Namespace is not null) {
                    throw new ParseException("Namespace already defined", scanner.Cursor.Position);
                }

                if (mark.Length > 0) {
                    throw new ParseException("Namespace must be the first term", scanner.Cursor.Position);
                }

                mark.Length++;
                mark.Namespace = @namespace;
                continue;
            }

            var @enum = Enum.Parse(mark, scanner);
            if (@enum is not null) {
                mark.Length++;
                mark.Enums ??= new();
                mark.Enums.Add(@enum.Name, @enum);
                continue;
            }

            var table = Entity.Parse(mark, scanner);
            if (table is not null) {
                mark.Length++;
                mark.Tables ??= new();
                mark.Tables?.Add(table.Name, table);
                continue;
            }

            var trait = Trait.Parse(mark, scanner);
            if (trait is not null) {
                mark.Length++;
                mark.Traits ??= new();
                mark.Traits?.Add(trait.Name, trait);
                continue;
            }

            throw new ParseException($"unexpected char {scanner.Cursor.Current}", scanner.Cursor.Position);
        }

        if (mark.Length <= 0) {
            return null;
        }

        return mark;
    }
}
