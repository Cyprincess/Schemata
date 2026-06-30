using System.Collections.Generic;
using Schemata.Common;
using Schemata.Core.Tests.Fixtures;
using Xunit;

namespace Schemata.Core.Tests.Common;

public class ResourceNameDescriptorShould
{
    [Fact]
    public void ParseCanonicalName_ExtractLeafAndParentValues() {
        var descriptor = ResourceNameDescriptor.ForType<Book>();

        var result = descriptor.ParseCanonicalName("publishers/acme/books/les-miserables");

        Assert.NotNull(result);
        Assert.Equal("les-miserables", result.Value.LeafName);
        Assert.Equal("acme", result.Value.ParentValues["publisher"]);
    }

    [Fact]
    public void Resolve_UseBareTitleCaseForParentProperty() {
        var descriptor = ResourceNameDescriptor.ForType<Book>();
        var book       = new Book { Name = "les-miserables", Publisher = "acme" };

        var resolved = descriptor.Resolve(book);

        Assert.Equal("publishers/acme/books/les-miserables", resolved);
    }

    [Fact]
    public void BuildParentPredicate_ReferenceBareTitleCaseProperty() {
        var descriptor = ResourceNameDescriptor.ForType<Book>();
        var values     = new Dictionary<string, string> { ["publisher"] = "acme" };

        var predicate = descriptor.BuildParentPredicate<Book>(values);

        Assert.NotNull(predicate);
        var compiled = predicate!.Compile();
        Assert.True(compiled(new() { Publisher  = "acme" }));
        Assert.False(compiled(new() { Publisher = "other" }));
    }

    [Fact]
    public void CollectionPath_IncludeParentSegments() {
        var descriptor = ResourceNameDescriptor.ForType<Book>();

        Assert.Equal("publishers/{publisher}/books", descriptor.CollectionPath);
    }

    [Fact]
    public void Collection_BeLowercasePluralOfTypeName() {
        var descriptor = ResourceNameDescriptor.ForType<Book>();

        Assert.Equal("books", descriptor.Collection);
    }

    [Fact]
    public void Plural_BePascalCasePluralOfTypeName() {
        var descriptor = ResourceNameDescriptor.ForType<Book>();

        Assert.Equal("Books", descriptor.Plural);
    }

    [Fact]
    public void HasParent_BeTrue_WhenParentSegmentsExist() {
        var descriptor = ResourceNameDescriptor.ForType<Book>();

        Assert.True(descriptor.HasParent);
    }

    [Fact]
    public void ParseCanonicalName_ReturnNull_WhenNoPattern() {
        var descriptor = ResourceNameDescriptor.ForType<Widget>();

        var result = descriptor.ParseCanonicalName("anything/here");

        Assert.Null(result);
    }

    [Fact]
    public void ParseParent_ExtractParentValues() {
        var descriptor = ResourceNameDescriptor.ForType<Book>();

        var result = descriptor.ParseParent("publishers/acme");

        Assert.NotNull(result);
        Assert.Equal("acme", result["publisher"]);
    }

    [Fact]
    public void ParseParent_ReturnNull_WhenNoParents() {
        var descriptor = ResourceNameDescriptor.ForType<Widget>();

        var result = descriptor.ParseParent("anything");

        Assert.Null(result);
    }

    [Fact]
    public void Pattern_MatchCanonicalNameAttribute() {
        var descriptor = ResourceNameDescriptor.ForType<Book>();

        Assert.Equal("publishers/{publisher}/books/{book}", descriptor.Pattern);
    }

    [Fact]
    public void Singular_PreserveDisplayNameThatSingularizesToDifferentValue() {
        var descriptor = ResourceNameDescriptor.ForType<Process>();

        Assert.Equal("Process", descriptor.Singular);
    }

    [Fact]
    public void Plural_PluralizePreservedSingular() {
        var descriptor = ResourceNameDescriptor.ForType<Process>();

        Assert.Equal("Processes", descriptor.Plural);
    }

    [Fact]
    public void Resolve_MapLeafToName_ForDisplayNameThatOverSingularizes() {
        var descriptor = ResourceNameDescriptor.ForType<Process>();

        var resolved = descriptor.Resolve(new Process { Name = "p1" });

        Assert.Equal("processes/p1", resolved);
    }

    [Fact]
    public void Resolve_MapParentToBareProperty_AndLeafToName_ForNestedResource() {
        var descriptor = ResourceNameDescriptor.ForType<ProcessStep>();

        var resolved = descriptor.Resolve(new ProcessStep { Process = "p1", Name = "s1" });

        Assert.Equal("processes/p1/steps/s1", resolved);
    }
}
