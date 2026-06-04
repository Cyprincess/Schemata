using Xunit;

namespace Schemata.Common.Tests;

public class SchemataNamingShould
{
    [Theory]
    [InlineData("FullName", "full_name")]
    [InlineData("PostalCode", "postal_code")]
    [InlineData("EntityTag", "entity_tag")]
    public void ToWireName_ConvertsClrMemberNameToSnakeCase(string source, string expected) {
        Assert.Equal(expected, SchemataNaming.ToWireName(source));
    }

    [Theory]
    [InlineData("full_name", "FullName")]
    [InlineData("postal_code", "PostalCode")]
    [InlineData("etag", "Etag")]
    public void ToClrMemberName_ConvertsSnakeCaseToPascalCase(string source, string expected) {
        Assert.Equal(expected, SchemataNaming.ToClrMemberName(source));
    }
}
