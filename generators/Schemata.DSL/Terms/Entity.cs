using System.Collections.Generic;
using Parlot;
using static System.StringComparison;

namespace Schemata.DSL.Terms;

public class Entity : TermBase
{
    public string Name { get; set; } = null!;

    public Note? Note { get; set; }

    public List<Use>? Uses { get; set; }

    public Dictionary<string, Enum>? Enums { get; set; }

    public Dictionary<string, Field>? Fields { get; set; }

    public Dictionary<string, Index>? Indices { get; set; }

    public Dictionary<string, Field>? Keys { get; set; }

    public Dictionary<string, Object>? Objects { get; set; }

    public Dictionary<string, Trait>? Traits { get; set; }

    // Entity = "Entity" WS Name [ [WS] : Name { [WS] , [WS] Name } ] [WS] LC [ Note | Enum | Trait | Dto | Index | Use | Field ] RC
    public static Entity? Parse(Mark mark, Scanner scanner) {
        if (!scanner.ReadText(nameof(Entity), InvariantCultureIgnoreCase)) return null;

        scanner.SkipWhiteSpace();

        return Parse<Entity>(mark, scanner);
    }

    protected static T Parse<T>(Mark mark, Scanner scanner)
        where T : Entity, new() {
        if (!scanner.ReadIdentifier(out var name)) {
            throw new ParseException("Expected a name", scanner.Cursor.Position);
        }

        var table = new T { Name = name.GetText() };

        // TODO: inherit

        SkipWhiteSpaceOrCommentOrNewLine(scanner);

        if (scanner.ReadChar('{')) {
            while (true) {
                SkipWhiteSpaceOrCommentOrNewLine(scanner);
                if (scanner.ReadChar('}')) break;

                var note = Note.Parse(mark, scanner);
                if (note != null) {
                    table.Note += note;
                    continue;
                }

                if (table.ParseEnum(mark, scanner)) {
                    continue;
                }

                if (table.ParseTrait(mark, scanner)) {
                    continue;
                }

                if (table.ParseObject(mark, scanner)) {
                    continue;
                }

                if (table.ParseIndex(mark, scanner)) {
                    continue;
                }

                var uses = Use.Parse(mark, scanner);
                if (uses != null) {
                    table.Uses ??= new List<Use>();
                    foreach (var use in uses) {
                        table.Uses.Add(use);

                        // TODO: move to Generator
                        var entity = mark.Traits?[use.Name];
                        if (entity == null) {
                            throw new ParseException($"Unknown trait {use.Name}", scanner.Cursor.Position);
                        }

                        if (entity.Fields != null) {
                            table.Fields ??= new Dictionary<string, Field>();
                            foreach (var kv in entity.Fields) {
                                if (table.Fields.ContainsKey(kv.Key)) {
                                    throw new ParseException($"Duplicate field name {kv.Key}", scanner.Cursor.Position);
                                }

                                table.Fields.Add(kv.Key, kv.Value);
                            }
                        }

                        if (entity.Indices != null) {
                            table.Indices ??= new Dictionary<string, Index>();
                            foreach (var kv in entity.Indices) {
                                if (table.Indices.ContainsKey(kv.Key)) {
                                    throw new ParseException($"Duplicate index name {kv.Key}", scanner.Cursor.Position);
                                }

                                table.Indices.Add(kv.Key, kv.Value);
                            }
                        }

                        if (entity.Keys != null) {
                            table.Keys ??= new Dictionary<string, Field>();
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
                if (field != null) {
                    table.Fields ??= new Dictionary<string, Field>();
                    if (table.Fields.ContainsKey(field.Name)) {
                        throw new ParseException($"Duplicate field name {field.Name}", scanner.Cursor.Position);
                    }

                    table.Fields.Add(field.Name, field);
                    continue;
                }

                throw new ParseException($"Unexpected char {scanner.Cursor.Current}", scanner.Cursor.Position);
            }
        }

        return table;
    }

    protected virtual bool ParseEnum(Mark mark, Scanner scanner) {
        var @enum = Enum.Parse(mark, scanner);
        if (@enum == null) return false;

        Enums ??= new Dictionary<string, Enum>();
        Enums.Add(@enum.Name, @enum);

        mark.Enums ??= new Dictionary<string, Enum>();
        mark.Enums.Add($"{Name}.{@enum.Name}", @enum);

        return true;
    }

    protected virtual bool ParseTrait(Mark mark, Scanner scanner) {
        var trait = Trait.Parse(mark, scanner);
        if (trait == null) return false;

        Traits ??= new Dictionary<string, Trait>();
        Traits.Add(trait.Name, trait);

        mark.Traits ??= new Dictionary<string, Trait>();
        mark.Traits.Add($"{Name}.{trait.Name}", trait);

        return true;
    }

    protected virtual bool ParseObject(Mark mark, Scanner scanner) {
        var @object = Object.Parse(mark, scanner);
        if (@object == null) return false;

        Objects ??= new Dictionary<string, Object>();
        Objects.Add(@object.Name, @object);

        mark.Objects ??= new Dictionary<string, Object>();
        mark.Objects.Add($"{Name}.{@object.Name}", @object);

        return true;
    }

    protected virtual bool ParseIndex(Mark mark, Scanner scanner) {
        var index = Index.Parse(mark, this, scanner);
        if (index == null) return false;

        Indices ??= new Dictionary<string, Index>();
        if (Indices.TryGetValue(index.Name, out var origin)) {
            try {
                index = origin + index;
            } catch (System.NotSupportedException) {
                throw new ParseException($"Duplicate index name {index.Name}", scanner.Cursor.Position);
            }
        }

        Indices[index.Name] = index;
        return true;
    }
}
