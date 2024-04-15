using System;
using System.Collections.Generic;
using System.Linq;
using Parlot;
using static System.StringComparison;

namespace Schemata.DSL.Terms;

public class Index : TermBase, INamedTerm
{
    public Entity Table { get; set; } = null!;

    public List<string> Fields { get; set; } = [];

    public List<Option>? Options { get; set; }

    public Note? Note { get; set; }

    #region INamedTerm Members

    public string Name => Fields.Aggregate($"IX_{Table.Name}", (s, f) => s + "_" + Utilities.ToCamelCase(f));

    #endregion

    // Index = "Index" WS Name { WS Name } [ [WS] LB [ Option { [WS] , [WS] Option } ] RB ] [ [WS] LC Note RC]
    // Index.Option = "Unique" | "BTree" | "B Tree" | "Hash"
    public static Index? Parse(Mark mark, Entity table, Scanner scanner) {
        if (!scanner.ReadText(nameof(Index), InvariantCultureIgnoreCase)) {
            return null;
        }

        scanner.SkipWhiteSpace();

        if (!scanner.ReadIdentifier(out var field)) {
            throw new ParseException("Expected a name", scanner.Cursor.Position);
        }

        var index = new Index {
            Table  = table,
            Fields = { field.GetText() },
        };

        while (true) {
            scanner.SkipWhiteSpace();

            if (!scanner.ReadIdentifier(out var name)) {
                break;
            }

            index.Fields.Add(name.GetText());
        }

        SkipWhiteSpaceOrCommentOrNewLine(scanner);

        foreach (var option in ParseOptions(mark, scanner)) {
            switch (option.Name) {
                case Constants.Options.Unique:
                case Constants.Options.BTree:
                case Constants.Options.Hash:
                    break;
                default:
                    throw new ParseException($"Unexpected option {option.Name}", scanner.Cursor.Position);
            }

            index.Options ??= [];
            index.Options.Add(option);
        }

        SkipWhiteSpaceOrCommentOrNewLine(scanner);

        if (scanner.ReadChar('{')) {
            while (true) {
                SkipWhiteSpaceOrCommentOrNewLine(scanner);
                if (scanner.ReadChar('}')) {
                    break;
                }

                var property = Property.Parse(mark, scanner);
                if (property?.Name == nameof(Note)) {
                    index.Note += new Note { Comment = property.Body };
                    continue;
                }

                if (property is not null) {
                    throw new ParseException($"Invalid property {property.Name}", scanner.Cursor.Position);
                }

                throw new ParseException($"Unexpected char {scanner.Cursor.Current}", scanner.Cursor.Position);
            }
        }

        return index;
    }

    public static Index operator +(Index a, Index b) {
        if (a.Name != b.Name || a.Table != b.Table || !a.Fields.SequenceEqual(b.Fields)) {
            throw new NotSupportedException("Unexpected merge operation between two Index");
        }

        if (a.Options is null) {
            return b;
        }

        if (b.Options is null) {
            return a;
        }

        return new() {
            Table   = a.Table,
            Fields  = a.Fields,
            Options = a.Options.Union(b.Options).ToList(),
            Note    = a.Note + b.Note,
        };
    }
}
