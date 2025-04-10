using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace Schemata.DSL.Tests;

public class ParserShould
{
    [Fact]
    public async Task Parser_InputValidSkmFile_ReturnExpression() {
        var file       = File.OpenRead("vector1.skm");
        var parser     = await Parser.ReadAsync(file);
        var expression = parser.Parse();

        Assert.NotNull(expression);
    }
}
