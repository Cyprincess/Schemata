using System.Text;
using Microsoft.CodeAnalysis;
using Schemata.Modeling.Generator.Expressions;

// ReSharper disable once CheckNamespace
namespace Schemata.Modeling.Generator;

public static class DocumentGenerator
{
    public static void Generate(SourceProductionContext spc, Document doc) {
        GenerateEnums(spc, doc);

        GenerateTraits(spc, doc);

        GenerateEntities(spc, doc);
    }

    private static void GenerateEnums(SourceProductionContext spc, Document doc) {
        foreach (var @enum in doc.Enumerations) {
            var sb = new StringBuilder();
            EnumGenerator.Generate(sb, @enum, doc);
            spc.AddSource($"{@enum.Name}", sb.ToString());
        }
    }

    private static void GenerateTraits(SourceProductionContext spc, Document doc) {
        foreach (var trait in doc.Traits) {
            TraitGenerator.Generate(spc, trait, doc);
        }
    }

    private static void GenerateEntities(SourceProductionContext spc, Document doc) {
        foreach (var entity in doc.Entities) {
            EntityGenerator.Generate(spc, entity, doc);
        }
    }
}
