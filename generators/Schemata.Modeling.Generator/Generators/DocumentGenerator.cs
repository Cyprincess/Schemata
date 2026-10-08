using System.Text;
using Microsoft.CodeAnalysis;
using Schemata.Modeling.Generator.Expressions;

// ReSharper disable once CheckNamespace
namespace Schemata.Modeling.Generator;

internal static class DocumentGenerator
{
    public static void Generate(SourceProductionContext spc, Document doc, Compilation compilation, Location location) {
        GenerateEnums(spc, doc);

        GenerateTraits(spc, doc);

        GenerateEntities(spc, doc, compilation, location);
    }

    private static void GenerateEnums(SourceProductionContext spc, Document doc) {
        foreach (var @enum in doc.Enumerations) {
            var sb = new StringBuilder();
            EnumGenerator.Generate(sb, @enum, doc);
            spc.AddSource(SourceIdentity.Hint("enum", doc.Namespace, @enum.Name), sb.ToString());
        }
    }

    private static void GenerateTraits(SourceProductionContext spc, Document doc) {
        foreach (var trait in doc.Traits) {
            TraitGenerator.Generate(spc, trait, doc);
        }
    }

    private static void GenerateEntities(SourceProductionContext spc, Document doc, Compilation compilation, Location location) {
        foreach (var entity in doc.Entities) {
            EntityGenerator.Generate(spc, entity, doc, compilation, location);
        }
    }
}
