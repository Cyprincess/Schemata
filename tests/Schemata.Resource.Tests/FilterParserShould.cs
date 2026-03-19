using System.Linq;
using Schemata.Abstractions.Entities;
using Schemata.Resource.Foundation.Grammars;
using Xunit;

namespace Schemata.Resource.Tests;

public class FilterParserShould
{
    [Fact]
    public void ParseOrder_WithValidExpression_ReturnsOrderingDictionary() {
        var result1 = Parser.Order.Parse("a,b")?.ToDictionary(kv => kv.Key.Value.Value!.ToString()!, kv => kv.Value);
        Assert.NotNull(result1);
        Assert.Equal(new() { ["a"] = Ordering.Ascending, ["b"] = Ordering.Ascending }, result1);

        var result2 = Parser.Order.Parse("a DESC,b")
                           ?.ToDictionary(kv => kv.Key.Value.Value!.ToString()!, kv => kv.Value);
        Assert.NotNull(result2);
        Assert.Equal(new() { ["a"] = Ordering.Descending, ["b"] = Ordering.Ascending }, result2);

        var result3 = Parser.Order.Parse("a,b DESC")
                           ?.ToDictionary(kv => kv.Key.Value.Value!.ToString()!, kv => kv.Value);
        Assert.NotNull(result3);
        Assert.Equal(new() { ["a"] = Ordering.Ascending, ["b"] = Ordering.Descending }, result3);
    }

    [Fact]
    public void ParseFilter_WithSimpleAndExpression_ReturnsExpression() {
        var expression = Parser.Filter.Parse("a b AND c AND d");
        Assert.NotNull(expression);
        Assert.Equal("[AND {\"a\" \"b\"} \"c\" \"d\"]", expression.ToString());
        Assert.True(expression.IsConstant);
    }

    [Fact]
    public void ParseFilter_WithSimpleOrExpression_ReturnsExpression() {
        var expression = Parser.Filter.Parse("New York Giants OR Yankees");
        Assert.NotNull(expression);
        Assert.Equal("{\"New\" \"York\" [OR \"Giants\" \"Yankees\"]}", expression.ToString());
        Assert.True(expression.IsConstant);
    }

    [Fact]
    public void ParseFilter_WithNumericComparison_ReturnsExpression() {
        var expression = Parser.Filter.Parse("a < 10 OR a >= 100");
        Assert.NotNull(expression);
        Assert.Equal("[OR [< \"a\" 10] [>= \"a\" 100]]", expression.ToString());
        Assert.False(expression.IsConstant);
    }

    [Fact]
    public void ParseFilter_WithNestedProperty_ReturnsExpression() {
        var expression = Parser.Filter.Parse("expr.type_map.1.type");
        Assert.NotNull(expression);
        Assert.Equal("\"expr\".\"type_map\".1.\"type\"", expression.ToString());
        Assert.False(expression.IsConstant);
    }

    [Fact]
    public void ParseFilter_WithFunctionCall_ReturnsExpression() {
        var expression = Parser.Filter.Parse("(msg.endsWith('world') AND retries < 10)");
        Assert.NotNull(expression);
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
    [InlineData("experiment.rollout <= cohort(request.user)",
                "[<= \"experiment\".\"rollout\" \"cohort\"(\"request\".\"user\")]", false)]
    [InlineData("prod", "\"prod\"", true)]
    [InlineData("regex(m.key, '^.*prod.*$')", "\"regex\"(\"m\".\"key\",\"^.*prod.*$\")", false)]
    [InlineData("math.mem('30mb')", "\"math\".\"mem\"(\"30mb\")", false)]
    [InlineData("(msg.endsWith('world') AND retries < 10)", "[AND \"msg\".\"endsWith\"(\"world\") [< \"retries\" 10]]",
                false)]
    [InlineData("(endsWith(msg, 'world') AND retries < 10)", "[AND \"endsWith\"(\"msg\",\"world\") [< \"retries\" 10]]",
                false)]
    [InlineData("time.now()", "\"time\".\"now\"()", false)]
    [InlineData("timestamp(\"2012-04-21T11:30:00-04:00\")", "\"timestamp\"(\"2012-04-21T11:30:00-04:00\")", false)]
    [InlineData("duration(\"32s\")", "\"duration\"(\"32s\")", false)]
    [InlineData("duration(\"4h0m0s\")", "\"duration\"(\"4h0m0s\")", false)]
    [InlineData(
        @"start_time > timestamp(""2006-01-02T15:04:05+07:00"") AND (driver = ""driver1"" OR start_driver = ""driver1"" OR end_driver = ""driver1"")",
        @"[AND [> ""start_time"" ""timestamp""(""2006-01-02T15:04:05+07:00"")] [OR [= ""driver"" ""driver1""] [= ""start_driver"" ""driver1""] [= ""end_driver"" ""driver1""]]]",
        false)]
    [InlineData("annotations:schedule", "[: \"annotations\" \"schedule\"]", false)]
    [InlineData("annotations.schedule = \"test\"", "[= \"annotations\".\"schedule\" \"test\"]", false)]
    public void ParseFilter_WithVariousExpressions_ReturnsExpectedExpression(
        string input,
        string expected,
        bool   constant
    ) {
        var expression = Parser.Filter.Parse(input);
        Assert.NotNull(expression);
        Assert.Equal(expected, expression.ToString());
        Assert.Equal(constant, expression.IsConstant);
    }

    [Theory]
    [InlineData("a = 1", "[= \"a\" 1]")]
    [InlineData("a != 1", "[!= \"a\" 1]")]
    [InlineData("a < 10", "[< \"a\" 10]")]
    [InlineData("a <= 10", "[<= \"a\" 10]")]
    [InlineData("a > 10", "[> \"a\" 10]")]
    [InlineData("a >= 10", "[>= \"a\" 10]")]
    [InlineData("a : 'foo'", "[: \"a\" \"foo\"]")]
    public void ParseAllComparators(string input, string expected) {
        var expression = Parser.Filter.Parse(input);
        Assert.NotNull(expression);
        Assert.Equal(expected, expression.ToString());
        Assert.False(expression.IsConstant);
    }

    [Theory]
    [InlineData("a = 1 AND b = 2")]
    [InlineData("a = 1 OR b = 2")]
    [InlineData("NOT a = 1")]
    [InlineData("-a")]
    [InlineData("a b")]
    [InlineData("(a OR b) AND c")]
    public void ParseLogicOperators(string input) {
        var result = Parser.Filter.Parse(input);
        Assert.NotNull(result);
    }

    [Fact]
    public void ParseLogicOperator_And_ReturnsExpectedStructure() {
        var expression = Parser.Filter.Parse("a = 1 AND b = 2");
        Assert.NotNull(expression);
        Assert.Equal("[AND [= \"a\" 1] [= \"b\" 2]]", expression.ToString());
    }

    [Fact]
    public void ParseLogicOperator_Or_ReturnsExpectedStructure() {
        var expression = Parser.Filter.Parse("a = 1 OR b = 2");
        Assert.NotNull(expression);
        Assert.Equal("[OR [= \"a\" 1] [= \"b\" 2]]", expression.ToString());
    }

    [Fact]
    public void ParseLogicOperator_Not_ReturnsExpectedStructure() {
        var expression = Parser.Filter.Parse("NOT a = 1");
        Assert.NotNull(expression);
        Assert.Equal("NOT [= \"a\" 1]", expression.ToString());
    }

    [Fact]
    public void ParseLogicOperator_Minus_ReturnsExpectedStructure() {
        var expression = Parser.Filter.Parse("-a");
        Assert.NotNull(expression);
        Assert.Equal("- \"a\"", expression.ToString());
    }

    [Fact]
    public void ParseLogicOperator_ImplicitAnd_ReturnsExpectedStructure() {
        var expression = Parser.Filter.Parse("a b");
        Assert.NotNull(expression);
        Assert.Equal("{\"a\" \"b\"}", expression.ToString());
        Assert.True(expression.IsConstant);
    }

    [Fact]
    public void ParseLogicOperator_CompositeWithExplicitAnd_ReturnsExpectedStructure() {
        var expression = Parser.Filter.Parse("(a OR b) AND c");
        Assert.NotNull(expression);
        Assert.Equal("[AND [OR \"a\" \"b\"] \"c\"]", expression.ToString());
    }

    [Theory]
    [InlineData("a = 42", "[= \"a\" 42]", false)]
    [InlineData("a = 'hello'", "[= \"a\" \"hello\"]", false)]
    [InlineData("a = foo", "[= \"a\" \"foo\"]", false)]
    public void ParseValueTypes(string input, string expected, bool constant) {
        var expression = Parser.Filter.Parse(input);
        Assert.NotNull(expression);
        Assert.Equal(expected, expression.ToString());
        Assert.Equal(constant, expression.IsConstant);
    }

    [Theory]
    [InlineData("a = TRUE", "[= \"a\" \u2611]")]
    [InlineData("a = FALSE", "[= \"a\" \u2612]")]
    [InlineData("a = true", "[= \"a\" \u2611]")]
    [InlineData("a = false", "[= \"a\" \u2612]")]
    [InlineData("a = True", "[= \"a\" \u2611]")]
    public void ParseTruthValue(string input, string expected) {
        var expression = Parser.Filter.Parse(input);
        Assert.NotNull(expression);
        Assert.Equal(expected, expression.ToString());
    }

    [Theory]
    [InlineData("a and b", "[AND \"a\" \"b\"]")]
    [InlineData("a or b", "[OR \"a\" \"b\"]")]
    [InlineData("not a", "NOT \"a\"")]
    [InlineData("a = null", "[= \"a\" \u2205]")]
    public void ParseCaseInsensitiveKeywords(string input, string expected) {
        var expression = Parser.Filter.Parse(input);
        Assert.NotNull(expression);
        Assert.Equal(expected, expression.ToString());
    }

    [Theory]
    [InlineData("ANDROID", "\"ANDROID\"")]
    [InlineData("ORDER", "\"ORDER\"")]
    [InlineData("NOTHING", "\"NOTHING\"")]
    [InlineData("NOTICE", "\"NOTICE\"")]
    [InlineData("TRUEBLOOD", "\"TRUEBLOOD\"")]
    [InlineData("NULLABLE", "\"NULLABLE\"")]
    [InlineData("ANDROID = 1", "[= \"ANDROID\" 1]")]
    [InlineData("NOTHING = 1", "[= \"NOTHING\" 1]")]
    public void ParseIdentifierWithKeywordPrefix(string input, string expected) {
        var expression = Parser.Filter.Parse(input);
        Assert.NotNull(expression);
        Assert.Equal(expected, expression.ToString());
    }

    [Fact]
    public void ParseValueType_Null() {
        var expression = Parser.Filter.Parse("a = NULL");
        Assert.NotNull(expression);
        Assert.Contains("\"a\"", expression.ToString());
        Assert.False(expression.IsConstant);
    }

    [Fact]
    public void ParseValueType_Decimal() {
        var expression = Parser.Filter.Parse("a = 3.14");
        Assert.NotNull(expression);
        Assert.Equal("[= \"a\" 3.14]", expression.ToString());
        Assert.False(expression.IsConstant);
    }

    [Fact]
    public void ParseDeeplyNested() {
        var result = Parser.Filter.Parse("((a OR b) AND (c OR d))");
        Assert.NotNull(result);
        Assert.Equal("[AND [OR \"a\" \"b\"] [OR \"c\" \"d\"]]", result.ToString());
    }

    [Theory]
    [InlineData("a.b.c = 1", "[= \"a\".\"b\".\"c\" 1]")]
    [InlineData("expr.type_map.1.type = 'bar'", "[= \"expr\".\"type_map\".1.\"type\" \"bar\"]")]
    public void ParseMemberTraversal(string input, string expected) {
        var expression = Parser.Filter.Parse(input);
        Assert.NotNull(expression);
        Assert.Equal(expected, expression.ToString());
        Assert.False(expression.IsConstant);
    }

    [Theory]
    [InlineData("msg.endsWith('world')", "\"msg\".\"endsWith\"(\"world\")")]
    [InlineData("regex(m.key, '^.*prod.*$')", "\"regex\"(\"m\".\"key\",\"^.*prod.*$\")")]
    [InlineData("time.now()", "\"time\".\"now\"()")]
    public void ParseFunctionCalls(string input, string expected) {
        var expression = Parser.Filter.Parse(input);
        Assert.NotNull(expression);
        Assert.Equal(expected, expression.ToString());
        Assert.False(expression.IsConstant);
    }

    [Theory]
    [InlineData("foo()", "\"foo\"()")]
    [InlineData("foo.bar()", "\"foo\".\"bar\"()")]
    [InlineData("foo.bar.baz('a')", "\"foo\".\"bar\".\"baz\"(\"a\")")]
    public void ParseFunctionWithNameBasedPath(string input, string expected) {
        var expression = Parser.Filter.Parse(input);
        Assert.NotNull(expression);
        Assert.Equal(expected, expression.ToString());
    }

    [Theory]
    [InlineData("a = 3.14", "[= \"a\" 3.14]")]
    [InlineData("a = -0.5", "[= \"a\" -0.5]")]
    [InlineData("a = 42", "[= \"a\" 42]")]
    [InlineData("a = 0.1", "[= \"a\" 0.1]")]
    public void ParseNumericValues(string input, string expected) {
        var expression = Parser.Filter.Parse(input);
        Assert.NotNull(expression);
        Assert.Equal(expected, expression.ToString());
    }

    [Theory]
    [InlineData("a = hello", "[= \"a\" \"hello\"]")]
    [InlineData("a = 'hello'", "[= \"a\" \"hello\"]")]
    [InlineData("hello", "\"hello\"")]
    [InlineData("'hello'", "\"hello\"")]
    public void ParseTextAndStringValues(string input, string expected) {
        var expression = Parser.Filter.Parse(input);
        Assert.NotNull(expression);
        Assert.Equal(expected, expression.ToString());
    }
}
