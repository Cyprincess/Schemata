using Schemata.Abstractions.Exceptions;
using Schemata.Security.Foundation.Signatures;
using Xunit;

namespace Schemata.Security.Tests;

public class MessageComponentIdentifierShould
{
    [Theory]
    [InlineData("@method", "\"@method\"")]
    [InlineData("content-digest", "\"content-digest\"")]
    [InlineData("Content-Digest", "\"content-digest\"")]
    [InlineData("\"@query-param\";name=\"Pet\"", "\"@query-param\";name=\"Pet\"")]
    [InlineData("\"example-header\";bs", "\"example-header\";bs")]
    [InlineData("\"expires\";tr", "\"expires\";tr")]
    [InlineData("\"@authority\";req", "\"@authority\";req")]
    [InlineData("\"example-dict\";key=\"a\"", "\"example-dict\";key=\"a\"")]
    public void Round_Trip_Textual_Identifiers(string text, string canonical) {
        var identifier = MessageComponentIdentifier.Parse(text);

        Assert.Equal(canonical, identifier.ToString());
        Assert.Equal(canonical, MessageComponentIdentifier.Parse(identifier.ToString()).ToString());
    }

    [Fact]
    public void Expose_Flags_And_String_Parameters() {
        var identifier = MessageComponentIdentifier.Parse("\"@query-param\";name=\"Pet\";req");

        Assert.True(identifier.IsDerived);
        Assert.Equal("Pet", identifier.ParameterName);
        Assert.True(identifier.IsFromRequest);
        Assert.False(identifier.IsBinaryWrapped);
        Assert.False(identifier.IsTrailer);
    }

    [Fact]
    public void Round_Trip_A_Quoted_Parameter_Carrying_Delimiters() {
        var identifier = MessageComponentIdentifier.Parse("\"example-dict\";key=\"a;b=c\"");

        Assert.Equal("a;b=c", identifier.GetParameter("key"));
        Assert.Equal("\"example-dict\";key=\"a;b=c\"", identifier.ToString());
        Assert.Equal(identifier.ToString(), MessageComponentIdentifier.Parse(identifier.ToString()).ToString());
    }

    [Fact]
    public void Round_Trip_An_Escaped_Quote_Inside_A_Parameter_Value() {
        var identifier = MessageComponentIdentifier.Parse("\"@query-param\";name=\"a\\\"b\"");

        Assert.Equal("a\"b", identifier.ParameterName);
        Assert.Equal("\"@query-param\";name=\"a\\\"b\"", identifier.ToString());
        Assert.Equal(identifier.ToString(), MessageComponentIdentifier.Parse(identifier.ToString()).ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("\"@method")]
    [InlineData("\"@method\"name")]
    [InlineData("@query-param;name=Pet")]
    [InlineData("@method;")]
    public void Reject_Malformed_Identifiers(string text) {
        Assert.Throws<InvalidArgumentException>(() => MessageComponentIdentifier.Parse(text));
    }
}
