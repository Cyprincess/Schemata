using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Schemata.Abstractions.Advisors;
using Xunit;

namespace Schemata.Advice.Generator.Tests;

public sealed class AdviceRunnerGenerationShould
{
    private const string Infrastructure = """
        namespace Schemata.Advice
        {
            public readonly struct AdvicePipeline<TAdvisor>
                where TAdvisor : Schemata.Abstractions.Advisors.IAdvisor;
        }
        """;

    [Fact]
    public void Emit_Arity_Classes_One_Through_Sixteen_That_Compile() {
        var output = Generate();

        var errors = output.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Empty(errors);

        var runners = output.SyntaxTrees.Where(tree => tree.FilePath.Contains("AdviceRunner.Arity", StringComparison.Ordinal)).ToList();
        Assert.Equal(16, runners.Count);

        AssertRunner(runners, 1,
                     "public static class AdviceRunner<TAdvisor, T1>",
                     "where TAdvisor : IAdvisor<T1>",
                     [1]);

        AssertRunner(runners, 8,
                     "public static class AdviceRunner<TAdvisor, T1, T2, T3, T4, T5, T6, T7, T8>",
                     "where TAdvisor : IAdvisor<T1, T2, T3, T4, T5, T6, T7, T8>",
                     [1, 2, 3, 4, 5, 6, 7, 8]);

        AssertRunner(runners, 16,
                     "public static class AdviceRunner<TAdvisor, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16>",
                     "where TAdvisor : IAdvisor<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16>",
                     [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16]);
    }

    [Fact]
    public void Skip_Runner_Emission_When_The_Compilation_Only_References_AdvicePipeline() {
        var output = Generate(definesInfrastructure: false);

        Assert.NotNull(output.GetTypeByMetadataName("Schemata.Advice.AdvicePipeline`1"));
        Assert.Empty(output.SyntaxTrees);
    }

    private static void AssertRunner(
        IEnumerable<SyntaxTree> runners,
        int                    arity,
        string                 declaration,
        string                 constraint,
        int[]                  arguments
    ) {
        var source = runners.Single(tree => tree.FilePath.EndsWith($"AdviceRunner.Arity{arity}.g.cs", StringComparison.Ordinal))
                            .ToString();

        Assert.Contains(declaration, source);
        Assert.Contains(constraint, source);
        Assert.Contains("public static async Task<AdviseResult> RunAsync(", source);

        foreach (var argument in arguments) {
            Assert.Contains($"T{argument} a{argument},", source);
        }

        var forwarded = string.Join(", ", arguments.Select(argument => $"a{argument}"));
        Assert.Contains($"await advisor.AdviseAsync(ctx, {forwarded}, ct);", source);
        Assert.Contains("result is not AdviseResult.Continue", source);
    }

    private static Compilation Generate(bool definesInfrastructure = true) {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
                         .Split(Path.PathSeparator)
                         .Select(path => MetadataReference.CreateFromFile(path))
                         .Cast<MetadataReference>()
                         .ToList();

        references.Add(MetadataReference.CreateFromFile(typeof(IAdvisor).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions).Assembly.Location));

        var parse = new CSharpParseOptions(LanguageVersion.Preview);
        var trees = new List<SyntaxTree>();

        if (definesInfrastructure) {
            trees.Add(CSharpSyntaxTree.ParseText(Infrastructure, parse));
        } else {
            references.Add(MetadataReference.CreateFromFile(typeof(AdvicePipeline<>).Assembly.Location));
        }

        var compilation = CSharpCompilation.Create(
            "AdviceRunners" + Guid.NewGuid().ToString("N"),
            trees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        GeneratorDriver driver = CSharpGeneratorDriver.Create([new AdvicePipelineGenerator().AsSourceGenerator()], parseOptions: parse);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        return output;
    }
}
