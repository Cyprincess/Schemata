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
                              .Where(static info => info is not null);

        var combined = advisors.Combine(hasInfrastructure);

        context.RegisterSourceOutput(combined, static (spc, pair) => {
            var (info, has) = pair;

            if (has && info is not null) {
                GenerateSource(spc, info);
            }
        });
    }

    #endregion

    private static bool IsAdvisorCandidate(SyntaxNode node) {
        if (node is not InterfaceDeclarationSyntax iface) {
            return false;
        }

        if (iface.BaseList is null) {
            return false;
        }

        foreach (var baseType in iface.BaseList.Types) {
            var text = baseType.Type.ToString();
            if (text.Contains("IAdvisor")) {
                return true;
            }
        }

        return false;
    }

    private static AdvisorInterfaceInfo? GetAdvisorInfo(GeneratorSyntaxContext ctx) {
        var iface = (InterfaceDeclarationSyntax)ctx.Node;
        if (ctx.SemanticModel.GetDeclaredSymbol(iface) is not INamedTypeSymbol symbol) {
            return null;
        }

        INamedTypeSymbol? advisorInterface = null;
        foreach (var ai in symbol.AllInterfaces) {
            if (ai.OriginalDefinition.Name != "IAdvisor" || !ai.IsGenericType) {
                continue;
            }

            advisorInterface = ai;
            break;
        }

        foreach (var directBase in symbol.Interfaces) {
            if (directBase.OriginalDefinition.Name != "IAdvisor" || !directBase.IsGenericType) {
                continue;
            }

            advisorInterface = directBase;
            break;
        }

        if (advisorInterface is null) {
            return null;
        }

        var advisorTypeArgs = advisorInterface.TypeArguments;

        var typeParams      = new List<string>();
        var typeConstraints = new List<string>();

        foreach (var tp in symbol.TypeParameters) {
            typeParams.Add(tp.Name);

            var constraints = new List<string>();

            if (tp.HasReferenceTypeConstraint) {
                constraints.Add("class");
            }

            if (tp.HasValueTypeConstraint) {
                constraints.Add("struct");
            }

            if (tp.HasUnmanagedTypeConstraint) {
                constraints.Add("unmanaged");
            }

            if (tp.HasNotNullConstraint) {
                constraints.Add("notnull");
            }

            foreach (var ct in tp.ConstraintTypes) {
                constraints.Add(ct.ToDisplayString(FullyQualified));
            }

            if (tp.HasConstructorConstraint) {
                constraints.Add("new()");
            }

            if (constraints.Count > 0) {
                typeConstraints.Add($"where {tp.Name} : {string.Join(", ", constraints)}");
            }
        }

        string constructedAdvisorType;
        if (symbol.TypeParameters.Length > 0) {
            var paramNames = string.Join(", ", symbol.TypeParameters.Select(tp => tp.Name));
            constructedAdvisorType
                = $"{
                    symbol.ToDisplayString(FullyQualified)
                          .Replace(
                               $"<{
                                   string.Join(
                                       ", ", symbol.TypeParameters.Select(tp => tp.ToDisplayString(FullyQualified)))
                               }>", $"<{paramNames}>")
                }";
        } else {
            constructedAdvisorType = symbol.ToDisplayString(FullyQualified);
        }

        var methodParams = new List<string> { "global::Schemata.Abstractions.Advisors.AdviceContext ctx" };

        var callArgs = new List<string> { "ctx" };

        for (var i = 0; i < advisorTypeArgs.Length; i++) {
            var argType   = ResolveTypeArgDisplay(advisorTypeArgs[i], symbol.TypeParameters);
            var paramName = $"a{i + 1}";
            methodParams.Add($"{argType} {paramName}");
            callArgs.Add(paramName);
        }

        methodParams.Add("global::System.Threading.CancellationToken ct = default");
        callArgs.Add("ct");

        var runnerTypeArgs = new List<string> { constructedAdvisorType };
        foreach (var t in advisorTypeArgs) {
            runnerTypeArgs.Add(ResolveTypeArgDisplay(t, symbol.TypeParameters));
        }

        var result = new AdvisorInterfaceInfo(symbol.ToDisplayString(FullyQualified),
                                              symbol.Name
                                            + (symbol.TypeParameters.Length > 0
                                                  ? "_" + string.Join("_", symbol.TypeParameters.Select(tp => tp.Name))
                                                  : ""), constructedAdvisorType);

        result.InterfaceTypeParameters.AddRange(typeParams);
        result.InterfaceTypeConstraints.AddRange(typeConstraints);
        result.AdvisorTypeArguments.AddRange(runnerTypeArgs);
        result.RunMethodParameters.AddRange(methodParams);
        result.RunMethodArguments.AddRange(callArgs);

        return result;
    }

    private static string ResolveTypeArgDisplay(
        ITypeSymbol                          typeArg,
        ImmutableArray<ITypeParameterSymbol> interfaceTypeParams
    ) {
        foreach (var tp in interfaceTypeParams) {
            if (SymbolEqualityComparer.Default.Equals(typeArg, tp)) {
                return typeArg.NullableAnnotation == NullableAnnotation.Annotated
                    ? tp.Name + "?"
                    : tp.Name;
            }
        }

        return typeArg.ToDisplayString(FullyQualified);
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

        var hintName = info.InterfaceMinimalName.Replace("global::", "")
                           .Replace("<", "_")
                           .Replace(">", "_")
                           .Replace(", ", "_")
                           .Replace(".", "_");
        spc.AddSource($"{hintName}.g.cs", sb.ToString());
    }
}
