using System.Text;
using Microsoft.CodeAnalysis;
using Schemata.DSL.Terms;

// ReSharper disable once CheckNamespace
namespace Schemata.DSL;

public static class MarkGenerator
{
    public static void Generate(this Mark mark, SourceProductionContext spc) {
        GenerateEnums(spc, mark);

        GenerateTraits(spc, mark);

        GenerateEntities(spc, mark);
    }

    private static void GenerateEnums(SourceProductionContext spc, Mark mark) {
        if (mark.Enums is null) {
            return;
        }

        foreach (var @enum in mark.Enums) {
            var scope = @enum.Key;
            var name  = @enum.Value.Name;
            scope = scope.Remove(scope.Length - name.Length - 1, name.Length + 1);
            if (!string.IsNullOrWhiteSpace(scope)) {
                if (mark.Tables?.ContainsKey(scope) == true) {
                    continue;
                }
            }

            var sb = new StringBuilder();
            @enum.Value.Generate(sb, mark);
            spc.AddSource($"{@enum.Value.Name}", sb.ToString());
        }
    }

    private static void GenerateTraits(SourceProductionContext spc, Mark mark) {
        if (mark.Traits is null) {
            return;
        }

        foreach (var trait in mark.Traits) {
            trait.Value.Generate(spc, mark);
        }
    }

    private static void GenerateEntities(SourceProductionContext spc, Mark mark) {
        if (mark.Tables is null) {
            return;
        }

        foreach (var table in mark.Tables) {
            table.Value.Generate(spc, mark);
        }
    }
}
