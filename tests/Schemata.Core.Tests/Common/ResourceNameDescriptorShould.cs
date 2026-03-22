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
}
