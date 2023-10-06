using System.Linq;
using Parlot;
using Schemata.DSL.Terms;
using Xunit;

namespace Schemata.DSL.Tests.TermsTests;

public class TestIndex
{
    [Theory]
    [InlineData("Index category_id", "IX_Post_CategoryId", new[] { "category_id" })]
    [InlineData("Index category_id[b tree]", "IX_Post_CategoryId", new[] { "category_id" }, new[] { Constants.Options.BTree })]
    [InlineData("Index user_id creation_date [hash] {Note FOOBAR}", "IX_Post_UserId_CreationDate", new[] { "user_id", "creation_date" }, new[] { Constants.Options.Hash }, "FOOBAR")]
    public void ShouldParseIndex(
        string    syntax,
        string    name,
        string[]  fields,
        string[]? options = null,
        string?   note    = null) {
        var mark    = new Mark();
        var entity  = new Entity { Name = "Post" };
        var scanner = new Scanner(syntax);
        var term    = Index.Parse(mark, entity, scanner);
        Assert.Equal(name, term?.Name);
        Assert.Equal(fields, term?.Fields);
        Assert.Equal(options, term?.Options?.Select(o => o.Name));
        Assert.Equal(note, term?.Note?.Comment);
    }
}
