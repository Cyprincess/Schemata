using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Schemata.Advice.Generator;

[Generator]
public class AdvicePipelineGenerator : IIncrementalGenerator
{
    private static readonly SymbolDisplayFormat FullyQualified = SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    #region IIncrementalGenerator Members

    public void Initialize(IncrementalGeneratorInitializationContext context) {
        var hasInfrastructure = context.CompilationProvider.Select(static (compilation, _) => compilation.GetTypeByMetadataName("Schemata.Advice.AdvicePipeline`1") is not null);

        var advisors = context.SyntaxProvider
                              .CreateSyntaxProvider(static (node, _) => IsAdvisorCandidate(node),
                                                    static (ctx,  _) => GetAdvisorInfo(ctx))
                              .Where(static info => info is not null)
                              .Collect();

        var deduped = advisors.Select(static (infos, _) => DedupeBySymbol(infos));

        var combined = deduped.Combine(hasInfrastructure);

        context.RegisterSourceOutput(combined, static (spc, pair) => {
            var (infos, has) = pair;

            if (!has) {
                return;
            }

            foreach (var info in infos) {
                GenerateSource(spc, info);
            }
        });
    }

    #endregion

    private static bool IsAdvisorCandidate(SyntaxNode node) {
        return node is InterfaceDeclarationSyntax { BaseList: not null };
    }

    private static ImmutableArray<AdvisorInterfaceInfo> DedupeBySymbol(
        ImmutableArray<AdvisorInterfaceInfo?> infos
    ) {
        if (infos.IsDefaultOrEmpty) {
            return ImmutableArray<AdvisorInterfaceInfo>.Empty;
        }

        var seen = new HashSet<string>(System.StringComparer.Ordinal);
        var result = ImmutableArray.CreateBuilder<AdvisorInterfaceInfo>(infos.Length);

        foreach (var info in infos) {
            if (info is null || !seen.Add(info.InterfaceMinimalName)) {
                continue;
            }

            result.Add(info);
        }

        return result.ToImmutable();
    }

    private static AdvisorInterfaceInfo? GetAdvisorInfo(GeneratorSyntaxContext ctx) {
        var iface = (InterfaceDeclarationSyntax)ctx.Node;
        if (ctx.SemanticModel.GetDeclaredSymbol(iface) is not INamedTypeSymbol symbol) {
            return null;
        }

        var advisorDefinition = ctx.SemanticModel.Compilation.GetTypeByMetadataName("Schemata.Abstractions.Advisors.IAdvisor");
        if (advisorDefinition is null) {
            return null;
        }

        INamedTypeSymbol? advisorInterface = null;
        foreach (var ai in symbol.AllInterfaces) {
            if (!IsAdvisorInterface(ai, advisorDefinition)) {
                continue;
            }

            advisorInterface = ai;
            break;
        }

        foreach (var directBase in symbol.Interfaces) {
            if (!IsAdvisorInterface(directBase, advisorDefinition)) {
                continue;
            }

            advisorInterface = directBase;
            break;
        }

        if (advisorInterface is null) {
            return null;
        }

        var advisorTypeArgs = advisorInterface.TypeArguments;
        var containing = new Stack<INamedTypeSymbol>();
        for (var current = symbol; current is not null; current = current.ContainingType) containing.Push(current);
        var parameters = containing.SelectMany(type => type.TypeParameters).ToImmutableArray();
        var names = new Dictionary<ITypeParameterSymbol, string>(SymbolEqualityComparer.Default);
        var used = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var parameter in parameters) {
            var name = parameter.Name;
            while (!used.Add(name)) name += "_";
            names.Add(parameter, name);
        }

        var typeParams      = new List<string>();
        var typeConstraints = new List<string>();

        foreach (var tp in parameters) {
            typeParams.Add(names[tp]);

            var constraints = new List<string>();

            if (tp.HasReferenceTypeConstraint) {
                constraints.Add(tp.ReferenceTypeConstraintNullableAnnotation == NullableAnnotation.Annotated ? "class?" : "class");
            }

            if (tp.HasValueTypeConstraint && !tp.HasUnmanagedTypeConstraint) {
                constraints.Add("struct");
            }

            if (tp.HasUnmanagedTypeConstraint) {
                constraints.Add("unmanaged");
            }

            if (tp.HasNotNullConstraint) {
                constraints.Add("notnull");
            }

            foreach (var ct in tp.ConstraintTypes) {
                constraints.Add(ResolveTypeArgDisplay(ct, names));
            }

            if (tp.HasConstructorConstraint) {
                constraints.Add("new()");
            }

            if (constraints.Count > 0) {
                typeConstraints.Add($"where {names[tp]} : {string.Join(", ", constraints)}");
            }
        }

        var constructedAdvisorType = ResolveTypeArgDisplay(symbol, names);

        var methodParams = new List<string> { "global::Schemata.Abstractions.Advisors.AdviceContext ctx" };

        var callArgs = new List<string> { "ctx" };

        for (var i = 0; i < advisorTypeArgs.Length; i++) {
            var argType   = ResolveTypeArgDisplay(advisorTypeArgs[i], names);
            var paramName = $"a{i + 1}";
            methodParams.Add($"{argType} {paramName}");
            callArgs.Add(paramName);
        }

        methodParams.Add("global::System.Threading.CancellationToken ct = default");
        callArgs.Add("ct");

        var runnerTypeArgs = new List<string> { constructedAdvisorType };
        foreach (var t in advisorTypeArgs) {
            runnerTypeArgs.Add(ResolveTypeArgDisplay(t, names));
        }

        var result = new AdvisorInterfaceInfo(symbol.ToDisplayString(FullyQualified),
                                              BuildSymbolKeyName(symbol),
                                              constructedAdvisorType);

        result.InterfaceTypeParameters.AddRange(typeParams);
        result.InterfaceTypeConstraints.AddRange(typeConstraints);
        result.AdvisorTypeArguments.AddRange(runnerTypeArgs);
        result.RunMethodParameters.AddRange(methodParams);
        result.RunMethodArguments.AddRange(callArgs);

        return result;
    }

    private static bool IsAdvisorInterface(INamedTypeSymbol type, INamedTypeSymbol advisorDefinition) {
        return type.IsGenericType
            && type.OriginalDefinition.Interfaces.Any(baseType => SymbolEqualityComparer.Default.Equals(baseType, advisorDefinition));
    }

    private static string ResolveTypeArgDisplay(ITypeSymbol type, IReadOnlyDictionary<ITypeParameterSymbol, string> names) {
        var text = new StringBuilder();
        foreach (var part in type.ToDisplayParts(FullyQualified)) {
            if (part.Symbol is ITypeParameterSymbol parameter && names.TryGetValue(parameter, out var name)) text.Append(name);
            else text.Append(part.ToString());
        }
        return text.ToString();
    }

    private static string BuildSymbolKeyName(INamedTypeSymbol symbol) {
        var segments = new List<string>
        {
            symbol.TypeKind.ToString(),
            symbol.ContainingNamespace.IsGlobalNamespace ? "" : symbol.ContainingNamespace.ToDisplayString(),
            BuildContainingTypesKey(symbol.ContainingType),
            symbol.Name,
            symbol.TypeParameters.Length.ToString(),
            string.Join(",", symbol.TypeParameters.Select(tp => tp.Name)),
        };

        var result = new StringBuilder();
        foreach (var segment in segments) {
            AppendHexLengthFramedSegment(result, segment);
        }

        return result.ToString();
    }

    private static string BuildContainingTypesKey(INamedTypeSymbol? containing) {
        if (containing is null) {
            return "";
        }

        var chain = new List<string>();
        for (var current = containing; current is not null; current = current.ContainingType) {
            chain.Add($"{current.Name}`{current.TypeParameters.Length}");
        }

        chain.Reverse();
        return string.Join("/", chain);
    }

    private static void AppendHexLengthFramedSegment(StringBuilder sb, string value) {
        var bytes = Encoding.UTF8.GetBytes(value);
        sb.Append(bytes.Length.ToString("x4"));
        foreach (var b in bytes) {
            sb.Append(b.ToString("x2"));
        }
    }

    private static void GenerateSource(SourceProductionContext spc, AdvisorInterfaceInfo info) {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("namespace Schemata.Advice;");
        sb.AppendLine();
        sb.AppendLine("public static partial class AdvicePipelineExtensions");
        sb.AppendLine("{");

        var typeParamsPart = info.InterfaceTypeParameters.Count > 0
            ? $"<{string.Join(", ", info.InterfaceTypeParameters)}>"
            : "";

        sb.AppendLine($"    public static global::System.Threading.Tasks.Task<global::Schemata.Abstractions.Advisors.AdviseResult> RunAsync{typeParamsPart}(");
        sb.AppendLine($"        this global::Schemata.Advice.AdvicePipeline<{info.ConstructedAdvisorType}> _,");

        for (var i = 0; i < info.RunMethodParameters.Count; i++) {
            var comma = i < info.RunMethodParameters.Count - 1 ? "," : ")";
            sb.AppendLine($"        {info.RunMethodParameters[i]}{comma}");
        }

        foreach (var constraint in info.InterfaceTypeConstraints) {
            sb.AppendLine($"        {constraint}");
        }

        var runnerTypeArgs = string.Join(", ", info.AdvisorTypeArguments);
        var callArgs       = string.Join(", ", info.RunMethodArguments);

        sb.AppendLine($"        => global::Schemata.Advice.AdviceRunner<{runnerTypeArgs}>.RunAsync({callArgs});");

        sb.AppendLine("}");

        spc.AddSource($"{info.InterfaceMinimalName}.g.cs", sb.ToString());
    }
}
