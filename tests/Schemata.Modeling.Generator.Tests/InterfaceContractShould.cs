using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Schemata.Abstractions.Entities;
using Xunit;

namespace Schemata.Modeling.Generator.Tests;

public class InterfaceContractShould
{
    [Fact]
    public void Compile_And_Execute_External_Timestamp_And_Local_Trait_Contracts() {
        var (compilation, result) = Generate("""
            Namespace Consumer
            Trait Labelled { string label }
            Entity Book {
                Use Labelled, Schemata.Abstractions.Entities.ITimestamp
            }
            """, """
            public static class Probe {
                public static string Run() {
                    var book = new Consumer.Book { Label = "title", CreateTime = new System.DateTime(2026, 1, 2) };
                    Schemata.Abstractions.Entities.ITimestamp timestamp = book;
                    return book.Label + ":" + timestamp.CreateTime.Value.Year;
                }
            }
            """);
        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Equal("title:2026", Execute(compilation));
    }

    [Fact]
    public void Compile_And_Execute_Base_List_External_Interface_Contract() {
        var (compilation, result) = Generate("""
            Namespace Consumer
            Entity AuditRow : Schemata.Abstractions.Entities.ITimestamp {
            }
            """, """
            public static class Probe {
                public static string Run() {
                    var row = new Consumer.AuditRow { CreateTime = new System.DateTime(2026, 3, 4), UpdateTime = new System.DateTime(2026, 5, 6) };
                    Schemata.Abstractions.Entities.ITimestamp timestamp = row;
                    return timestamp.CreateTime.Value.Year + ":" + timestamp.UpdateTime.Value.Month;
                }
            }
            """);
        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Equal("2026:5", Execute(compilation));
    }

    [Fact]
    public void Produce_The_Same_Contract_From_Use_And_Base_Syntax() {
        var (compilation, result) = Generate("""
            Namespace Consumer
            Trait Labelled { string label }
            Entity Used {
                Use Labelled, Schemata.Abstractions.Entities.ITimestamp
            }
            Entity Based : Labelled, Schemata.Abstractions.Entities.ITimestamp {
            }
            """, """
            public static class Probe {
                public static string Run() {
                    var used = new Consumer.Used { Label = "a", CreateTime = new System.DateTime(2026, 1, 2) };
                    var based = new Consumer.Based { Label = "b", CreateTime = new System.DateTime(2026, 1, 2) };
                    Schemata.Abstractions.Entities.ITimestamp usedTimestamp = used;
                    Schemata.Abstractions.Entities.ITimestamp basedTimestamp = based;
                    var same = usedTimestamp.CreateTime == basedTimestamp.CreateTime
                            && usedTimestamp.UpdateTime == basedTimestamp.UpdateTime;
                    return used.Label + based.Label + ":" + same;
                }
            }
            """);
        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Equal("ab:True", Execute(compilation));
    }

    [Theory]
    [InlineData("Missing.Contract", "", "Unresolved")]
    [InlineData("Contracts.IOpen", "public interface IOpen<T> { T Value { get; set; } }", "closed interface")]
    [InlineData("Contracts.NotInterface", "public class NotInterface { }", "requires an interface")]
    [InlineData("Contracts.IConflict", "public interface IA { string Value { get; } } public interface IB { int Value { get; } } public interface IConflict : IA, IB { }", "Conflicting interface property")]
    [InlineData("Contracts.Owner.IPrivate", "public class Owner { private interface IPrivate { int Value { get; } } }", "Inaccessible")]
    public void Diagnose_Base_List_Contracts_With_The_Use_Diagnostics(string contract, string declaration, string reason) {
        var (_, result) = Generate("Namespace Consumer\nEntity Book : " + contract + " { }", "namespace Contracts { " + declaration + " }");
        Assert.Null(result.Exception);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "SKM001" && diagnostic.GetMessage().Contains(reason, StringComparison.Ordinal));
    }

    [Fact]
    public void Compile_Diamond_And_Closed_Generic_Properties_Reusing_Compatible_Fields() {
        var (compilation, result) = Generate("""
            Namespace Consumer
            Entity Book {
                Use Contracts.IDiamond, Contracts.IValue<System.String>
                string name
            }
            """, """
            #nullable enable
            namespace Contracts {
                public interface IBase { string Name { get; set; } }
                public interface ILeft : IBase { }
                public interface IRight : IBase { }
                public interface IDiamond : ILeft, IRight { string? Optional { get; init; } }
                public interface IValue<T> { T Value { get; set; } }
            }
            public static class Probe {
                public static string Run() {
                    var book = new Consumer.Book { Name = "one", Value = "two", Optional = null };
                    return ((Contracts.IBase)book).Name + ":" + ((Contracts.IValue<string>)book).Value;
                }
            }
            """);
        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Equal("one:two", Execute(compilation));
    }

    [Theory]
    [InlineData("Missing.Contract", "", "Unresolved")]
    [InlineData("Contracts.IOpen", "public interface IOpen<T> { T Value { get; set; } }", "closed interface")]
    [InlineData("Contracts.NotInterface", "public class NotInterface { }", "requires an interface")]
    [InlineData("Contracts.IMethod", "public interface IMethod { void Run(); }", "Unsupported required member")]
    [InlineData("Contracts.IEvent", "public interface IEvent { event System.Action Changed; }", "Unsupported required member")]
    [InlineData("Contracts.IIndex", "public interface IIndex { string this[int i] { get; } }", "Unsupported required member")]
    [InlineData("Contracts.IStatic", "public interface IStatic { static abstract int Number { get; } }", "Unsupported required member")]
    [InlineData("Contracts.IBuffer", "public interface IBuffer { System.Span<int> Buffer { get; } }", "Unsupported required member")]
    [InlineData("Contracts.IConflict", "public interface IA { string Value { get; } } public interface IB { int Value { get; } } public interface IConflict : IA, IB { }", "Conflicting interface property")]
    public void Diagnose_Unsupported_Contracts_Without_Generator_Crash(string contract, string declaration, string reason) {
        var (_, result) = Generate("Namespace Consumer\nEntity Book { Use " + contract + " }", "namespace Contracts { " + declaration + " }");
        Assert.Null(result.Exception);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "SKM001" && diagnostic.GetMessage().Contains(reason, StringComparison.Ordinal));
    }

    [Fact]
    public void Same_Simple_Names_In_Different_Documents_Compile_Independently() {
        var (compilation, result) = Generate("Namespace First\nEntity Book { string title }",
            "public static class Probe { public static string Run() => new First.Book { Title = \"a\" }.Title + new Second.Book { Title = \"b\" }.Title; }",
            "Namespace Second\nEntity Book { string title }");
        Assert.Null(result.Exception);
        Assert.Equal("ab", Execute(compilation));
    }

    [Fact]
    public void Preserve_Default_Interface_Implementations_And_Compound_Generic_Arguments() {
        var (compilation, result) = Generate("Namespace Consumer\nEntity Book { Use Contracts.IDerived, Contracts.IValue<System.String?[]>, Contracts.Outer<System.Int32>.INested }", """
            #nullable enable
            namespace Contracts {
                public interface IBase { string Value { get; } void Run(); }
                public interface IDerived : IBase { string IBase.Value => "default"; void IBase.Run() { } }
                public interface IValue<T> { T Items { get; set; } }
                public class Outer<T> { public interface INested { T Number { get; set; } } }
            }
            public static class Probe {
                public static string Run() {
                    var book = new Consumer.Book { Items = new string?[] { null, "value" }, Number = 42 };
                    ((Contracts.IBase)book).Run();
                    return ((Contracts.IBase)book).Value + ":" + book.Items[1] + ":" + book.Number;
                }
            }
            """);
        Assert.Null(result.Exception);
        Assert.Equal("default:value:42", Execute(compilation));
    }

    [Fact]
    public void Reuse_Equivalent_Trait_Field_Aliases() {
        var (compilation, result) = Generate("Namespace Consumer\nTrait Identified { integer id }\nEntity Book { Use Identified int id }",
            "public static class Probe { public static string Run() => new Consumer.Book { Id = 7 }.Id.ToString(); }");
        Assert.Null(result.Exception);
        Assert.Equal("7", Execute(compilation));
    }

    [Fact]
    public void Reject_Inaccessible_Nested_Interface_And_Incomplete_Document() {
        var (_, inaccessible) = Generate("Namespace Consumer\nEntity Book { Use Contracts.Owner.IPrivate }",
            "namespace Contracts { public class Owner { private interface IPrivate { int Value { get; } } } }");
        Assert.Null(inaccessible.Exception);
        Assert.Contains(inaccessible.Diagnostics, diagnostic => diagnostic.Id == "SKM001" && diagnostic.GetMessage().Contains("Inaccessible", StringComparison.Ordinal));
        var (_, incomplete) = Generate("Entity Book { Use Broken< }", "");
        Assert.Null(incomplete.Exception);
        Assert.Contains(incomplete.Diagnostics, diagnostic => diagnostic.Id == "SKM002");
    }

    private static (Compilation Compilation, GeneratorRunResult Result) Generate(string dsl, string consumer, string? second = null) {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path)).Cast<MetadataReference>().ToList();
        references.Add(MetadataReference.CreateFromFile(typeof(ITimestamp).Assembly.Location));
        var parse = new CSharpParseOptions(LanguageVersion.CSharp12);
        var compilation = CSharpCompilation.Create("Consumer" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(consumer, parse)], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        var texts = second is null ? new AdditionalText[] { new Text("first.skm", dsl) } : [new Text("first.skm", dsl), new Text("second.skm", second)];
        GeneratorDriver driver = CSharpGeneratorDriver.Create([new Generator().AsSourceGenerator()], texts, parse);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        return (output, driver.GetRunResult().Results.Single());
    }

    private static string Execute(Compilation compilation) {
        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        var assembly = Assembly.Load(stream.ToArray());
        return (string)assembly.GetType("Probe")!.GetMethod("Run")!.Invoke(null, null)!;
    }

    private sealed class Text(string path, string content) : AdditionalText
    {
        public override string Path => path;
        public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(content);
    }
}
