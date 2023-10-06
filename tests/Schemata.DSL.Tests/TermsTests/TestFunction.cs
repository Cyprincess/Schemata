using System.Linq;
using Parlot;
using Schemata.DSL.Terms;
using Xunit;

namespace Schemata.DSL.Tests.TermsTests;

public class TestFunction
{
    [Theory]
    [InlineData("now()", "now")]
    [InlineData("obfuscate(email_address)", "obfuscate", new[] { "email_address" })]
    [InlineData("substring(input,index,length)", "substring", new[] { "input", "index", "length" })]
    [InlineData("substring( input , 0 , 5 )", "substring", new[] { "input", "0", "5" })]
    public void ShouldParseFunction(string syntax, string name, string[]? parameters = null) {
        var mark    = new Mark();
        var scanner = new Scanner(syntax);
        var term    = Function.Parse(mark, scanner);
        Assert.Equal(name, term?.Body);
        Assert.Equal(parameters, term?.Parameters?.Select(t => t.Body));
    }
}
