using System;
using System.Collections.Generic;
using Parlot;
using static System.StringComparison;

namespace Schemata.DSL.Terms;

public class Entity : TermBase, INamedTerm
{
    public Note? Note { get; set; }

    public List<Use>? Uses { get; set; }

    public Dictionary<string, Field>? Fields { get; set; }

    public Dictionary<string, Index>? Indices { get; set; }

    public Dictionary<string, Field>? Keys { get; set; }

    public Dictionary<string, Object>? Objects { get; set; }

    #region INamedTerm Members

    public string Name { get; set; } = null!;

    #endregion

    // Entity = "Entity" WS Name [ [WS] : Name { [WS] , [WS] Name } ] [WS] LC [ Note | Enum | Trait | Dto | Index | Use | Field ] RC
    public static Entity? Parse(Mark mark, Scanner scanner) {
        if (!scanner.ReadText(nameof(Entity), InvariantCultureIgnoreCase)) {
            return null;
        }

        scanner.SkipWhiteSpace();

        return Parse<Entity>(mark, scanner);
    }

    protected static T Parse<T>(Mark mark, Scanner scanner) where T : Entity, new() {
        if (!scanner.ReadIdentifier(out var name)) {
            throw new ParseException("Expected a name", scanner.Cursor.Position);
        }

        var table = new T { Name = name.GetText() };

        // TODO: inherit

        SkipWhiteSpaceOrCommentOrNewLine(scanner);

        if (!scanner.ReadChar('{')) {
            throw new ParseException($"Expected {typeof(T).Name} definition", scanner.Cursor.Position);
        }

        while (true) {
            SkipWhiteSpaceOrCommentOrNewLine(scanner);
            if (scanner.ReadChar('}')) {
                break;
            }

            var note = Note.Parse(mark, scanner);
            if (note is not null) {
                table.Note += note;
                continue;
            }

            if (table.ParseEnum(mark, scanner)) {
                continue;
            }

            if (table.ParseObject(mark, scanner)) {
                continue;
            }

            if (table.ParseIndex(mark, scanner)) {
                continue;
            }

            var uses = Use.Parse(mark, scanner);
            if (uses is not null) {
                table.Uses ??= [];
                foreach (var use in uses) {
                    table.Uses.Add(use);

                    var entity = mark.Traits?[use.Name];
                    if (entity is null) {
                        throw new ParseException($"Unknown trait {use.Name}", scanner.Cursor.Position);
                    }

                    if (entity.Fields is not null) {
                        table.Fields ??= new();
                        foreach (var kv in entity.Fields) {
                            if (table.Fields.ContainsKey(kv.Key)) {
                                throw new ParseException($"Duplicate field name {kv.Key}", scanner.Cursor.Position);
                            }

                            var inherited = new Field {
                                Inherited  = true,
                                Type       = kv.Value.Type,
                                Name       = kv.Value.Name,
                                Nullable   = kv.Value.Nullable,
                                Note       = kv.Value.Note,
                                Options    = kv.Value.Options,
                                Properties = kv.Value.Properties,
                            };

                            table.Fields.Add(kv.Key, inherited);
                        }
                    }

                    if (entity.Indices is not null) {
                        table.Indices ??= new();
                        foreach (var kv in entity.Indices) {
                            if (table.Indices.ContainsKey(kv.Key)) {
                                throw new ParseException($"Duplicate index name {kv.Key}", scanner.Cursor.Position);
                            }

                            table.Indices.Add(kv.Key, kv.Value);
                        }
                    }

                    if (entity.Keys is not null) {
                        table.Keys ??= new();
                        foreach (var kv in entity.Keys) {
                            if (table.Keys.ContainsKey(kv.Key)) {
                                throw new ParseException($"Duplicate key name {kv.Key}", scanner.Cursor.Position);
                            }

                            table.Keys.Add(kv.Key, kv.Value);
                        }
                    }
                }

                continue;
            }

            var field = Field.Parse(mark, table, scanner);
            if (field is not null) {
                table.Fields ??= new();
                if (table.Fields.ContainsKey(field.Name)) {
                    throw new ParseException($"Duplicate field name {field.Name}", scanner.Cursor.Position);
                }

                table.Fields.Add(field.Name, field);
                continue;
            }

            throw new ParseException($"Unexpected char {scanner.Cursor.Current}", scanner.Cursor.Position);
        }

        return table;
    }

    protected virtual bool ParseEnum(Mark mark, Scanner scanner) {
        var @enum = Enum.Parse(mark, scanner);
        if (@enum is null) {
            return false;
        }

        mark.Enums ??= new();
        mark.Enums.Add($"{Name}.{@enum.Name}", @enum);

        return true;
    }

    protected virtual bool ParseObject(Mark mark, Scanner scanner) {
        var @object = Object.Parse(mark, scanner);
        if (@object is null) {
            return false;
        }

        Objects ??= new();
        Objects.Add(@object.Name, @object);

        mark.Objects ??= new();
        mark.Objects.Add($"{Name}.{@object.Name}", @object);

        return true;
    }

    protected virtual bool ParseIndex(Mark mark, Scanner scanner) {
        var index = Index.Parse(mark, this, scanner);
        if (index is null) {
            return false;
        }

        Indices ??= new();
        if (Indices.TryGetValue(index.Name, out var origin)) {
            try {
                index = origin + index;
            } catch (NotSupportedException) {
                throw new ParseException($"Duplicate index name {index.Name}", scanner.Cursor.Position);
            }
        }

        Indices[index.Name] = index;
        return true;
    }
}
