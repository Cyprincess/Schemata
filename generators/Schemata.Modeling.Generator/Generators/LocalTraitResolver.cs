using System;
using Schemata.Modeling.Generator.Expressions;

namespace Schemata.Modeling.Generator;

internal static class LocalTraitResolver
{
    internal static bool TryResolve(Document document, string name, out Trait? trait, out string? interfaceName) {
        foreach (var candidate in document.Traits) {
            if (!string.Equals(candidate.Name, name, StringComparison.Ordinal)
                && (name.Length != candidate.Name.Length + 1 || name[0] != 'I'
                    || string.CompareOrdinal(name, 1, candidate.Name, 0, candidate.Name.Length) != 0)) {
                continue;
            }

            trait         = candidate;
            interfaceName = $"I{candidate.Name}";
            return true;
        }

        trait         = null;
        interfaceName = null;
        return false;
    }
}
