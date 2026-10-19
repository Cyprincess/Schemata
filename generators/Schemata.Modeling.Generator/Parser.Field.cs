using Schemata.Modeling.Generator.Expressions;

namespace Schemata.Modeling.Generator;

internal static partial class Parser
{
    private static FieldOption ParseFieldOption(string normalized, int offset) {
        return normalized switch {
            "required" or "notnull" => FieldOption.Required,
            "unique"                => FieldOption.Unique,
            "primarykey"            => FieldOption.PrimaryKey,
            "autoincrement"         => FieldOption.AutoIncrement,
            "btree"                 => FieldOption.BTree,
            "hash"                  => FieldOption.Hash,
            var _                   => throw new UnknownOptionException("field", normalized, offset),
        };
    }

    private static PointerOption ParsePointerOption(string normalized, int offset) {
        return normalized switch {
            "unique" => PointerOption.Unique,
            "btree"  => PointerOption.BTree,
            "hash"   => PointerOption.Hash,
            var _    => throw new UnknownOptionException("pointer", normalized, offset),
        };
    }
}
