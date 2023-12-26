using System.Linq;
using Parlot;
using Schemata.DSL.Terms;
using Xunit;

namespace Schemata.DSL.Tests.TermsTests;

public class TestField
{
    [Theory]
    [InlineData("Foo bar", "Foo", "bar")]
    [InlineData("Foo? bar", "Foo", "bar", true)]
    [InlineData("Foo bar []", "Foo", "bar")]
    [InlineData("Foo bar[required]", "Foo", "bar", false, null, new[] { "Required" })]
    [InlineData("Foo bar[not null, primary key, auto increment]", "Foo", "bar", false, null, new[] { "Required", "PrimaryKey", "AutoIncrement" })]
    [InlineData("Foo bar {}", "Foo", "bar")]
    [InlineData("Foo bar{Note FOOBAR}", "Foo", "bar", false, "FOOBAR")]
    [InlineData("string bar{default ''}", "string", "bar", false, null, null, new[] { "Default", "" })]
    [InlineData("""
                int bar{
                default 0
                length 11
                }
                """, "int", "bar", false, null, null, new[] { "Default", "0", "Length", "11" })]
    [InlineData("string bar[required]{default ''}", "string", "bar", false, null, new[] { "Required" }, new[] { "Default", "" })]
    public void ShouldParseField(
        string    syntax,
        string    type,
        string    name,
        bool      nullable   = false,
        string?   note       = null,
        string[]? options    = null,
        string[]? properties = null) {
        var mark    = new Mark();
        var scanner = new Scanner(syntax);
        var term    = Field.Parse(mark, null, scanner);
        Assert.Equal(type, term?.Type);
        Assert.Equal(name, term?.Name);
        Assert.Equal(nullable, term?.Nullable);
        Assert.Equal(note, term?.Note?.Comment);
        Assert.Equal(options ?? [], term?.Options?.Select(o => o.Name) ?? []);
        Assert.Equal(properties ?? [], term?.Properties?.SelectMany(p => new[] { p.Key, p.Value.Body }) ?? []);
    }
}
