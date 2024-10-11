using System.Linq;
using Parlot;
using Schemata.DSL.Terms;
using Xunit;

namespace Schemata.DSL.Tests.TermsTests;

public class TestObjectField
{
    [Theory]
    [InlineData("bar", null, "bar")]
    [InlineData("Foo bar", "Foo", "bar")]
    [InlineData("Foo? bar", "Foo", "bar", true)]
    [InlineData("Foo bar []", "Foo", "bar")]
    [InlineData("Foo bar[omit]", "Foo", "bar", false, null, new[] { "Omit" })]
    [InlineData("Foo bar {}", "Foo", "bar")]
    [InlineData("Foo bar{Note FOOBAR}", "Foo", "bar", false, "FOOBAR")]
    [InlineData("""
                Foo bar{
                id
                }
                """,
                "Foo",
                "bar",
                false,
                null,
                null,
                new[] { "id" })]
    [InlineData("id = bar.id", null, "id", false, null, null, null, "bar.id")]
    [InlineData("timestamp foo = now()", "timestamp", "foo", false, null, null, null, "now")]
    [InlineData("string foo = \"bar\"", "string", "foo", false, null, null, null, "bar")]
    public void ShouldParseObjectField(
        string    syntax,
        string    type,
        string    name,
        bool      nullable = false,
        string?   note     = null,
        string[]? options  = null,
        string[]? fields   = null,
        string?   map      = null) {
        var mark    = new Mark();
        var scanner = new Scanner(syntax);
        var term    = ObjectField.Parse(mark, null, scanner);
        Assert.Equal(type, term?.Type);
        Assert.Equal(name, term?.Name);
        Assert.Equal(nullable, term?.Nullable);
        Assert.Equal(note, term?.Note?.Comment);
        Assert.Equal(options ?? [], term?.Options?.Select(o => o.Name) ?? []);
        Assert.Equal(fields ?? [], term?.Fields?.Select(f => f.Key) ?? []);
        Assert.Equal(map, term?.Map?.Body);
    }
}
