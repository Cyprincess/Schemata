using System;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace Schemata.Modeling.Generator.Tests;

public class UnknownOptionShould
{
    [Theory]
    [InlineData("Entity Student {\n  string title [unsupported]\n}", "field", "unsupported")]
    [InlineData("Entity Student {\n  long category_id\n  Index category_id [unsupported]\n}", "pointer", "unsupported")]
    [InlineData("Entity Student {\n  Object response {\n    title [unsupported]\n  }\n}", "view", "unsupported")]
    public void Reject_Unknown_Options_With_The_Source_Offset_At_The_Parser_Boundary(string input, string kind, string name) {
        var exception = Assert.Throws<UnknownOptionException>(() => Parser.Document.Parse(input));
        Assert.Equal(kind, exception.Kind);
        Assert.Equal(name, exception.Name);
        Assert.Equal(input.IndexOf(name, StringComparison.Ordinal), exception.Offset);
    }

    [Theory]
    [InlineData("Namespace Consumer\n\nEntity Student {\n  string title [unsupported]\n}", 4)]
    [InlineData("Namespace Consumer\n\nEntity Student {\n  long category_id\n  Index category_id [unsupported]\n}", 5)]
    [InlineData("Namespace Consumer\n\nEntity Student {\n  Object response {\n    title [unsupported]\n  }\n}", 5)]
    public void Report_Unknown_Options_As_Skm002_At_The_File_And_Line_Without_Generator_Exception(string dsl, int line) {
        var result = Run(("first.skm", dsl));
        Assert.Null(result.Exception);
        var diagnostic = Assert.Single(result.Diagnostics, candidate => candidate.Id == "SKM002");
        var span = diagnostic.Location.GetLineSpan();
        Assert.Equal("first.skm", span.Path);
        Assert.Equal(line - 1, span.StartLinePosition.Line);
    }

    [Fact]
    public void Report_Syntax_Errors_With_The_Existing_File_Scope_Skm002() {
        var result = Run(("first.skm", "Entity Book { Use Broken< }"));
        Assert.Null(result.Exception);
        var diagnostic = Assert.Single(result.Diagnostics, candidate => candidate.Id == "SKM002");
        Assert.Equal(0, diagnostic.Location.GetLineSpan().StartLinePosition.Line);
    }

    [Fact]
    public void Keep_Generating_Compilable_Output_For_Known_Options() {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path)).Cast<MetadataReference>().ToList();
        var parse = new CSharpParseOptions(LanguageVersion.CSharp12);
        var compilation = CSharpCompilation.Create("KnownOptions" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText("", parse)], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create([new Generator().AsSourceGenerator()],
            [new Text("known.skm", "Namespace Consumer\n\nTrait Marker { string tag }\n\nEntity Student {\n  Use Marker\n  string title [primary key]\n}")],
            parse);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        var result = driver.GetRunResult().Results.Single();
        Assert.Null(result.Exception);
        Assert.Empty(result.Diagnostics);
        Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    private static GeneratorRunResult Run(params (string Path, string Content)[] documents) {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path)).Cast<MetadataReference>().ToList();
        var parse = new CSharpParseOptions(LanguageVersion.CSharp12);
        var compilation = CSharpCompilation.Create("UnknownOptions" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText("", parse)], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var texts = documents.Select(document => (AdditionalText)new Text(document.Path, document.Content)).ToArray();
        GeneratorDriver driver = CSharpGeneratorDriver.Create([new Generator().AsSourceGenerator()], texts, parse);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        return driver.GetRunResult().Results.Single();
    }

    private sealed class Text(string path, string content) : AdditionalText
    {
        public override string Path => path;
        public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(content);
    }
}
