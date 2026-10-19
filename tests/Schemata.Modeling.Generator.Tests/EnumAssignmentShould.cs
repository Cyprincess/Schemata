using System;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Schemata.Modeling.Generator.Tests;

public class EnumAssignmentShould
{
    [Fact]
    public void Accept_Integer_Literals_Within_Int32_Range() {
        var doc = Parser.Document.Parse("Enum Status { Draft = 0, Max = 2147483647 }");

        Assert.NotNull(doc);
        foreach (var value in doc!.Enumerations[0].Values) {
            Assert.True(DocumentGenerator.IsSupportedEnumAssignment(value.Assignment), value.Name);
        }
    }

    [Fact]
    public void Accept_Negative_Integer_Literals_Within_Int32_Range() {
        var doc = Parser.Document.Parse("Enum Status { Min = -2147483648, Draft = -1 }");

        Assert.NotNull(doc);
        foreach (var value in doc!.Enumerations[0].Values) {
            Assert.True(DocumentGenerator.IsSupportedEnumAssignment(value.Assignment), value.Name);
        }
    }

    [Fact]
    public void Accept_Unassigned_Members_Using_Automatic_Numbering() {
        var doc = Parser.Document.Parse("Enum Status { Draft, Published }");

        Assert.NotNull(doc);
        foreach (var value in doc!.Enumerations[0].Values) {
            Assert.True(DocumentGenerator.IsSupportedEnumAssignment(value.Assignment), value.Name);
        }
    }

    [Fact]
    public void Reject_Nonintegral_Or_Out_Of_Range_Or_NonNumeric_Assignments() {
        var doc = Parser.Document.Parse("Enum Status { Fractional = 1.5, Overflow = 3000000000, Text = 'json', Flag = true, Nothing = null }");

        Assert.NotNull(doc);
        foreach (var value in doc!.Enumerations[0].Values) {
            Assert.False(DocumentGenerator.IsSupportedEnumAssignment(value.Assignment), value.Name);
        }
    }

    [Fact]
    public void Reject_Underflow_Negative_Integer_Literals() {
        var doc = Parser.Document.Parse("Enum Status { Underflow = -2147483649 }");

        Assert.NotNull(doc);
        Assert.False(DocumentGenerator.IsSupportedEnumAssignment(doc!.Enumerations[0].Values[0].Assignment));
    }

    [Fact]
    public void Emit_Compiling_Source_With_The_Assigned_Constants() {
        var doc   = Assert.IsType<Expressions.Document>(Parser.Document.Parse("Namespace Test.Output\n\nEnum Status { Draft = 0, Published = 1 }"));
        var sb    = new StringBuilder();
        EnumGenerator.Generate(sb, doc.Enumerations[0], doc);

        var syntaxTree = CSharpSyntaxTree.ParseText(sb.ToString());
        var references = Assert.IsType<string>(AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
            .Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("EnumAssignment", [syntaxTree], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.DoesNotContain(compilation.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var model = compilation.GetSemanticModel(syntaxTree);
        var declaration = Assert.Single(syntaxTree.GetRoot().DescendantNodes().OfType<EnumDeclarationSyntax>());
        var symbol = Assert.IsAssignableFrom<INamedTypeSymbol>(model.GetDeclaredSymbol(declaration));

        Assert.Equal("Test.Output.Status", symbol.ToDisplayString());
        Assert.Equal(0, Assert.IsType<int>(symbol.GetMembers("Draft").OfType<IFieldSymbol>().Single().ConstantValue));
        Assert.Equal(1, Assert.IsType<int>(symbol.GetMembers("Published").OfType<IFieldSymbol>().Single().ConstantValue));
    }
}
