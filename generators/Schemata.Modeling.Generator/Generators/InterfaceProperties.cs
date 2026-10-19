using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Schemata.Modeling.Generator.Expressions;

namespace Schemata.Modeling.Generator;

internal static class InterfaceProperties
{
    private static readonly DiagnosticDescriptor InvalidContract = new("SKM001", "Invalid interface contract", "Entity '{0}': {1}", "Modeling", DiagnosticSeverity.Error, true);
    private static readonly SymbolDisplayFormat Display = SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
        SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    internal static bool Generate(SourceProductionContext context, StringBuilder output, Entity entity, Document document, Compilation compilation, Location location) {
        var fields = new Dictionary<string, Field>(StringComparer.Ordinal);
        var properties = new Dictionary<string, (IPropertySymbol Symbol, bool Get, bool Set, bool Init)>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var active = new HashSet<string>(StringComparer.Ordinal);
        var valid = true;
        void Fail(string message) {
            context.ReportDiagnostic(Diagnostic.Create(InvalidContract, location, entity.Name, message));
            valid = false;
        }
        var probeSource = "#nullable enable\n" + (string.IsNullOrEmpty(document.Namespace) ? "" : "namespace " + document.Namespace + " {")
            + "class __SchemataContractProbe { object __value; }" + (string.IsNullOrEmpty(document.Namespace) ? "" : "}");
        var probeTree = CSharpSyntaxTree.ParseText(probeSource, compilation.SyntaxTrees.FirstOrDefault()?.Options as CSharpParseOptions ?? CSharpParseOptions.Default);
        var probe = compilation.AddSyntaxTrees(probeTree);
        var model = probe.GetSemanticModel(probeTree);
        var position = probeTree.GetRoot().DescendantNodes().OfType<VariableDeclarationSyntax>().Single().Type.SpanStart;
        ITypeSymbol? Resolve(string name) {
            return model.GetSpeculativeTypeInfo(position, SyntaxFactory.ParseTypeName(name), SpeculativeBindingOption.BindAsTypeOrNamespace).Type;
        }
        void AddField(Field field) {
            var name = Utilities.ToCamelCase(field.Name);
            if (fields.TryGetValue(name, out var existing)) {
                var existingType = Utilities.GetClrType(existing.Type)?.FullName ?? existing.Type;
                var candidateType = Utilities.GetClrType(field.Type)?.FullName ?? field.Type;
                if (existingType != candidateType || existing.Nullable != field.Nullable) Fail("Conflicting field '" + name + "'.");
                return;
            }
            fields.Add(name, field);
        }
        void Visit(string name) {
            if (active.Contains(name)) { Fail("Cyclic trait '" + name + "'."); return; }
            if (!visited.Add(name)) return;
            var local = document.Traits.FirstOrDefault(trait => trait.Name == name);
            if (local is not null) {
                active.Add(name);
                foreach (var parent in local.Uses.SelectMany(use => use.QualifiedNames).Concat(local.Bases)) Visit(parent);
                foreach (var field in local.Fields) AddField(field);
                active.Remove(name);
                return;
            }
            var symbol = Resolve(name) as INamedTypeSymbol;
            if (symbol is null || symbol.TypeKind == TypeKind.Error) { Fail("Unresolved interface '" + name + "'."); return; }
            if (symbol.TypeKind != TypeKind.Interface) { Fail("Use requires an interface: '" + name + "'."); return; }
            if (symbol.IsUnboundGenericType || symbol.TypeArguments.Any(type => type.TypeKind is TypeKind.TypeParameter or TypeKind.Error)) {
                Fail("Use requires a closed interface: '" + name + "'."); return;
            }
            if (!probe.IsSymbolAccessibleWithin(symbol, probe.Assembly)) { Fail("Inaccessible interface '" + name + "'."); return; }
            foreach (var contract in symbol.AllInterfaces.Concat(new[] { symbol })) {
                foreach (var member in contract.GetMembers()) {
                    if (member is IMethodSymbol { MethodKind: MethodKind.PropertyGet or MethodKind.PropertySet or MethodKind.EventAdd or MethodKind.EventRemove }) continue;
                    if (!member.IsAbstract) continue;
                    if (symbol.FindImplementationForInterfaceMember(member) is { IsAbstract: false }) continue;
                    if (member is not IPropertySymbol property || property.IsIndexer || property.IsStatic || property.ReturnsByRef || property.ReturnsByRefReadonly
                        || property.Type.IsRefLikeType || property.Type.TypeKind is TypeKind.Pointer or TypeKind.FunctionPointer) {
                        Fail("Unsupported required member '" + member.ToDisplayString() + "'."); continue;
                    }
                    if (property.GetMethod is { DeclaredAccessibility: not Accessibility.Public } || property.SetMethod is { DeclaredAccessibility: not Accessibility.Public }) {
                        Fail("Unsupported inaccessible accessor '" + property.ToDisplayString() + "'."); continue;
                    }
                    if (properties.TryGetValue(property.Name, out var prior)) {
                        if (!SymbolEqualityComparer.IncludeNullability.Equals(prior.Symbol.Type, property.Type)
                            || prior.Set && property.SetMethod is not null && prior.Init != property.SetMethod.IsInitOnly) {
                            Fail("Conflicting interface property '" + property.Name + "'.");
                        } else {
                            properties[property.Name] = (prior.Symbol, prior.Get || property.GetMethod is not null,
                                prior.Set || property.SetMethod is not null, prior.Init || property.SetMethod?.IsInitOnly == true);
                        }
                    } else properties.Add(property.Name, (property, property.GetMethod is not null, property.SetMethod is not null, property.SetMethod?.IsInitOnly == true));
                }
            }
        }
        foreach (var field in entity.Fields) AddField(field);
        foreach (var name in entity.Uses.SelectMany(use => use.QualifiedNames)) Visit(name);
        foreach (var name in entity.Bases) Visit(name);
        foreach (var pair in properties) {
            if (!fields.TryGetValue(pair.Key, out var field)) continue;
            var type = Utilities.GetClrType(field.Type)?.FullName ?? field.Type;
            if (field.Nullable) type += "?";
            var fieldType = Resolve(type);
            if (fieldType?.IsReferenceType == true) fieldType = fieldType.WithNullableAnnotation(field.Nullable ? NullableAnnotation.Annotated : NullableAnnotation.NotAnnotated);
            if (!SymbolEqualityComparer.IncludeNullability.Equals(fieldType, pair.Value.Symbol.Type) || pair.Value.Init) {
                Fail("Field '" + pair.Key + "' is incompatible with interface property '" + pair.Value.Symbol.ToDisplayString() + "'.");
            }
        }
        if (!valid) return false;
        EntityGenerator.GenerateFields(output, fields.Values);
        foreach (var pair in properties) {
            if (fields.ContainsKey(pair.Key)) continue;
            var property = pair.Value.Symbol;
            var getter = pair.Value.Get ? "get; " : "private get; ";
            var setter = !pair.Value.Set ? "" : pair.Value.Init ? "init; " : "set; ";
            output.AppendLine("        public " + property.Type.ToDisplayString(Display) + " @" + property.Name + " { " + getter + setter + "} = default!;");
        }
        return true;
    }
}
