using System;
using Schemata.Modeling.Generator.Expressions;

namespace Schemata.Modeling.Generator;

public static partial class Parser
{
    private static FieldOption ParseFieldOption(string normalized) {
        return normalized switch {
            "required" or "notnull" => FieldOption.Required,
            "unique"                => FieldOption.Unique,
            "primarykey"            => FieldOption.PrimaryKey,
            "autoincrement"         => FieldOption.AutoIncrement,
            "btree"                 => FieldOption.BTree,
            "hash"                  => FieldOption.Hash,
            var _                   => throw new InvalidOperationException($"Unknown field option: {normalized}"),
        };
    }

    private static PointerOption ParsePointerOption(string normalized) {
        return normalized switch {
            "unique" => PointerOption.Unique,
            "btree"  => PointerOption.BTree,
            "hash"   => PointerOption.Hash,
            var _    => throw new InvalidOperationException($"Unknown pointer option: {normalized}"),
        };
    }
}
