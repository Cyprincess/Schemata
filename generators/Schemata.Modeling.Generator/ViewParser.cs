using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Schemata.Modeling.Generator.Expressions;

namespace Schemata.Modeling.Generator;

public static partial class Parser
{
    private static ViewField BuildViewField(
        string?                    type,
        bool                       nullable,
        string                     name,
        EquatableArray<ViewOption> options,
        IReadOnlyList<object>?     body,
        IExpression?               assignment
    ) {
        var notes    = ImmutableArray.CreateBuilder<Note>();
        var children = ImmutableArray.CreateBuilder<ViewField>();

        if (body != null) {
            for (var i = 0; i < body.Count; i++) {
                switch (body[i]) {
                    case Note n:
                        notes.Add(n);
                        break;
                    case ViewField vf:
                        children.Add(vf);
                        break;
                }
            }
        }

        return new(type, nullable, name, options, notes.ToImmutable(),
                   children.ToImmutable(), assignment);
    }

    private static ViewOption ParseViewOption(string normalized) {
        return normalized switch {
            "omit"    => ViewOption.Omit,
            "omitall" => ViewOption.OmitAll,
            var _     => throw new InvalidOperationException($"Unknown view option: {normalized}"),
        };
    }
}
