using Schemata.Resource.Foundation.Filters;
using Xunit;

namespace Schemata.Resource.Tests;

public class TestFilterParser
{
    [Fact]
    public void ParseExample1() {
        var expression = Parser.Filter.Parse("a b AND c AND d");

        Assert.Equal("[AND {\"a\" \"b\"} \"c\" \"d\"]", expression.ToString());
        Assert.True(expression.IsConstant);
    }

    [Fact]
    public void ParseExample2() {
        var expression = Parser.Filter.Parse("New York Giants OR Yankees");

        Assert.Equal("{\"New\" \"York\" [OR \"Giants\" \"Yankees\"]}", expression.ToString());
        Assert.True(expression.IsConstant);
    }

    [Fact]
    public void ParseExample3() {
        var expression = Parser.Filter.Parse("a < 10 OR a >= 100");

        Assert.Equal("[OR [< \"a\" 10] [>= \"a\" 100]]", expression.ToString());
        Assert.False(expression.IsConstant);
    }

    [Fact]
    public void ParseExample4() {
        var expression = Parser.Filter.Parse("expr.type_map.1.type");

        Assert.Equal("\"expr\".\"type_map\".1.\"type\"", expression.ToString());
        Assert.False(expression.IsConstant);
    }

    [Fact]
    public void ParseExample5() {
        var expression = Parser.Filter.Parse("(msg.endsWith('world') AND retries < 10)");

        Assert.Equal("[AND \"msg\".\"endsWith\"(\"world\") [< \"retries\" 10]]", expression.ToString());
        Assert.False(expression.IsConstant);
    }

    [Theory]
    [InlineData("New York (Giants OR Yankees)", "{\"New\" \"York\" [OR \"Giants\" \"Yankees\"]}", true)]
    [InlineData("(a b) AND c AND d", "[AND {\"a\" \"b\"} \"c\" \"d\"]", true)]
    [InlineData("a OR b OR c", "[OR \"a\" \"b\" \"c\"]", true)]
    [InlineData("NOT (a OR b)", "NOT [OR \"a\" \"b\"]", true)]
    [InlineData("-file:\".java\"", "- [: \"file\" \".java\"]", false)]
    [InlineData("package=com.google", "[= \"package\" \"com\".\"google\"]", false)]
    [InlineData("msg != 'hello'", "[!= \"msg\" \"hello\"]", false)]
    [InlineData("1 > 0", "[> 1 0]", false)]
    [InlineData("2.5 >= 2.4", "[>= 2.5 2.4]", false)]
    [InlineData("foo >= -2.4", "[>= \"foo\" -2.4]", false)]
    [InlineData("foo >= (-2.4)", "[>= \"foo\" - 2.4]", false)]
    [InlineData("yesterday < request.time", "[< \"yesterday\" \"request\".\"time\"]", false)]
    [InlineData("experiment.rollout <= cohort(request.user)", "[<= \"experiment\".\"rollout\" \"cohort\"(\"request\".\"user\")]", false)]
    [InlineData("prod", "\"prod\"", true)]
    [InlineData("regex(m.key, '^.*prod.*$')", "\"regex\"(\"m\".\"key\",\"^.*prod.*$\")", false)]
    [InlineData("math.mem('30mb')", "\"math\".\"mem\"(\"30mb\")", false)]
    [InlineData("(msg.endsWith('world') AND retries < 10)", "[AND \"msg\".\"endsWith\"(\"world\") [< \"retries\" 10]]", false)]
    [InlineData("(endsWith(msg, 'world') AND retries < 10)", "[AND \"endsWith\"(\"msg\",\"world\") [< \"retries\" 10]]", false)]
    [InlineData("time.now()", "\"time\".\"now\"()", false)]
    [InlineData("timestamp(\"2012-04-21T11:30:00-04:00\")", "\"timestamp\"(\"2012-04-21T11:30:00-04:00\")", false)]
    [InlineData("duration(\"32s\")", "\"duration\"(\"32s\")", false)]
    [InlineData("duration(\"4h0m0s\")", "\"duration\"(\"4h0m0s\")", false)]
    [InlineData(@"start_time > timestamp(""2006-01-02T15:04:05+07:00"") AND
        (driver = ""driver1"" OR start_driver = ""driver1"" OR end_driver = ""driver1"")",
        @"[AND [> ""start_time"" ""timestamp""(""2006-01-02T15:04:05+07:00"")] [OR [= ""driver"" ""driver1""] [= ""start_driver"" ""driver1""] [= ""end_driver"" ""driver1""]]]",
        false)]
    [InlineData("annotations:schedule", "[: \"annotations\" \"schedule\"]", false)]
    [InlineData("annotations.schedule = \"test\"", "[= \"annotations\".\"schedule\" \"test\"]", false)]
    public void ParseMoreVectors(string input, string expected, bool constant) {
        var expression = Parser.Filter.Parse(input);

        Assert.Equal(expected, expression?.ToString());
        Assert.Equal(constant, expression?.IsConstant);
    }
}
