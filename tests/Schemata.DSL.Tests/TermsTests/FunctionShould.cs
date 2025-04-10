using System.Linq;
using Parlot;
using Schemata.DSL.Terms;
using Xunit;

namespace Schemata.DSL.Tests.TermsTests;

public class FunctionShould
{
    [Theory]
    [InlineData("now()", "now")]
    [InlineData("obfuscate(email_address)", "obfuscate", new[] { "email_address" })]
    [InlineData("substring(input,index,length)", "substring", new[] { "input", "index", "length" })]
    [InlineData("substring( input , 0 , 5 )", "substring", new[] { "input", "0", "5" })]
    public void ParseFunction_WithValidSyntax_ReturnsCorrectExpression(
        string    syntax,
        string    name,
        string[]? parameters = null) {
        var mark    = new Mark();
        var scanner = new Scanner(syntax);
        var term    = Function.Parse(mark, scanner);

        Assert.NotNull(term);
        Assert.Equal(name, term.Body);
        Assert.Equal(parameters, term.Parameters?.Select(t => t.Body).ToArray());
    }
}
