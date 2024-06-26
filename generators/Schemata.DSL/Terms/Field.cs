using System;
using System.Collections.Generic;
using Parlot;

namespace Schemata.DSL.Terms;

public class Field : TermBase, INamedTerm
{
    public bool Inherited { get; set; }

    public string Type { get; set; } = null!;

    public bool Nullable { get; set; }

    public Note? Note { get; set; }

    public List<Option>? Options { get; set; }

    public Dictionary<string, Property>? Properties { get; set; }

    #region INamedTerm Members

    public string Name { get; set; } = null!;

    #endregion

    // Field = Type [ [WS] ? ] WS Name [ [WS] LB [ Option { [WS] , [WS] Option } ] RB ] [ [WS] LC {Note | Property} RC ]
    public static Field? Parse(Mark mark, Entity? table, Scanner scanner) {
        if (!scanner.ReadIdentifier(out var type)) {
            return null;
        }

        scanner.SkipWhiteSpace();

        var nullable = scanner.ReadChar('?');
        if (nullable) {
            scanner.SkipWhiteSpace();
        }

        if (!scanner.ReadIdentifier(out var name)) {
            throw new ParseException("Expected a name", scanner.Cursor.Position);
        }

        var field = new Field {
            Type     = NormalizeType(mark, table, type.GetText()),
            Name     = name.GetText(),
            Nullable = nullable,
        };

        SkipWhiteSpaceOrCommentOrNewLine(scanner);

        foreach (var option in ParseOptions(mark, scanner)) {
            switch (option.Name) {
                case SkmConstants.Options.AutoIncrement:
                case SkmConstants.Options.PrimaryKey:
                case SkmConstants.Options.Required:
                case SkmConstants.Options.Unique:
                case SkmConstants.Options.BTree:
                case SkmConstants.Options.Hash:
                    break;
                default:
                    throw new ParseException($"Unexpected option {option.Name}", scanner.Cursor.Position);
            }

            if (table is not null) {
                switch (option.Name) {
                    case SkmConstants.Options.PrimaryKey or SkmConstants.Options.AutoIncrement:
                    {
                        table.Keys ??= new();
                        table.Keys.Add(field.Name, field);
                        continue;
                    }
                    case SkmConstants.Options.Unique or SkmConstants.Options.BTree or SkmConstants.Options.Hash:
                    {
                        table.Indices ??= new();
                        var index = new Index {
                            Table   = table,
                            Fields  = { field.Name },
                            Options = [option],
                        };
                        if (table.Indices.TryGetValue(index.Name, out var origin)) {
                            try {
                                index = origin + index;
                            } catch (NotSupportedException) {
                                throw new ParseException($"Duplicate index name {index.Name}", scanner.Cursor.Position);
                            }
                        }

                        table.Indices[index.Name] = index;
                        continue;
                    }
                }
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

                var property = Property.Parse(mark, scanner);

                if (property?.Name == nameof(Note)) {
                    field.Note += new Note { Comment = property.Body };
                    continue;
                }

                if (property is null) {
                    throw new ParseException($"Unexpected char {scanner.Cursor.Current}", scanner.Cursor.Position);
                }

                field.Properties ??= new();
                field.Properties.Add(property.Name, property);
            }
        }

        return field;
    }
}
