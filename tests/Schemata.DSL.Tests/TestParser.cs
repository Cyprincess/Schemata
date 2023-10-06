using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace Schemata.DSL.Tests;

public class TestParser
{
    [Fact]
    public async Task ParseExample1() {
        var file       = File.OpenRead("vector1.skm");
        var parser     = await Parser.ReadAsync(file);
        var expression = parser.Parse();
    }
}
