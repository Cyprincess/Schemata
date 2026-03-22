using Schemata.Resource.Foundation.Grammars;
using Xunit;

namespace Schemata.Resource.Foundation.Tests.Filtering;

public class FilterParserShould
{
    [Theory]
    [InlineData("New York Giants", """{"New" "York" "Giants"}""", true)]
    [InlineData("New York Giants OR Yankees", """{"New" "York" [OR "Giants" "Yankees"]}""", true)]
    [InlineData("New York (Giants OR Yankees)", """{"New" "York" [OR "Giants" "Yankees"]}""", true)]
    [InlineData("a b AND c AND d", """[AND {"a" "b"} "c" "d"]""", true)]
    [InlineData("(a b) AND c AND d", """[AND {"a" "b"} "c" "d"]""", true)]
    [InlineData("a OR b OR c", """[OR "a" "b" "c"]""", true)]
    [InlineData("NOT (a OR b)", """NOT [OR "a" "b"]""", true)]
    [InlineData("prod", @"""prod""", true)]
    [InlineData("-file:\".java\"", """- [: "file" ".java"]""", false)]
    [InlineData("-30", "- 30", true)]
    [InlineData("a < 10 OR a >= 100", """[OR [< "a" 10] [>= "a" 100]]""", false)]
    [InlineData("1 > 0", "[> 1 0]", false)]
    [InlineData("2.5 >= 2.4", "[>= 2.5 2.4]", false)]
    [InlineData("foo >= -2.4", """[>= "foo" -2.4]""", false)]
    [InlineData("foo >= (-2.4)", """[>= "foo" - 2.4]""", false)]
    [InlineData("-2.5 >= -2.4", "- [>= 2.5 -2.4]", false)]
    [InlineData("package=com.google", """[= "package" "com"."google"]""", false)]
    [InlineData("yesterday < request.time", """[< "yesterday" "request"."time"]""", false)]
    [InlineData("expr.type_map.1.type", @"""expr"".""type_map"".1.""type""", false)]
    [InlineData("experiment.rollout <= cohort(request.user)",
                """[<= "experiment"."rollout" "cohort"("request"."user")]""", false)]
    [InlineData("regex(m.key, '^.*prod.*$')", @"""regex""(""m"".""key"",""^.*prod.*$"")", false)]
    [InlineData("math.mem('30mb')", @"""math"".""mem""(""30mb"")", false)]
    [InlineData("(msg.endsWith('world') AND retries < 10)", """[AND "msg"."endsWith"("world") [< "retries" 10]]""",
                false)]
    [InlineData("(endsWith(msg, 'world') AND retries < 10)", """[AND "endsWith"("msg","world") [< "retries" 10]]""",
                false)]
    [InlineData("time.now()", @"""time"".""now""()", false)]
    [InlineData("timestamp(\"2012-04-21T11:30:00-04:00\")", @"""timestamp""(""2012-04-21T11:30:00-04:00"")", false)]
    [InlineData("duration(\"32s\")", @"""duration""(""32s"")", false)]
    [InlineData("duration(\"4h0m0s\")", @"""duration""(""4h0m0s"")", false)]
    [InlineData("msg != 'hello'", """[!= "msg" "hello"]""", false)]
    [InlineData("msg != \"hello\"", """[!= "msg" "hello"]""", false)]
    [InlineData("annotations:schedule", """[: "annotations" "schedule"]""", false)]
    [InlineData("annotations.schedule = \"test\"", """[= "annotations"."schedule" "test"]""", false)]
    [InlineData(
        "start_time > timestamp(\"2006-01-02T15:04:05+07:00\") AND (driver = \"driver1\" OR start_driver = \"driver1\" OR end_driver = \"driver1\")",
        """[AND [> "start_time" "timestamp"("2006-01-02T15:04:05+07:00")] [OR [= "driver" "driver1"] [= "start_driver" "driver1"] [= "end_driver" "driver1"]]]""",
        false)]
    [InlineData("a = 1", """[= "a" 1]""", false)]
    [InlineData("a != 1", """[!= "a" 1]""", false)]
    [InlineData("a < 10", """[< "a" 10]""", false)]
    [InlineData("a <= 10", """[<= "a" 10]""", false)]
    [InlineData("a > 10", """[> "a" 10]""", false)]
    [InlineData("a >= 10", """[>= "a" 10]""", false)]
    [InlineData("a : 'foo'", """[: "a" "foo"]""", false)]
    [InlineData("a = 1 AND b = 2", """[AND [= "a" 1] [= "b" 2]]""", false)]
    [InlineData("a = 1 OR b = 2", """[OR [= "a" 1] [= "b" 2]]""", false)]
    [InlineData("NOT a = 1", """NOT [= "a" 1]""", false)]
    [InlineData("-a", @"- " + "\"" + @"a" + "\"", true)]
    [InlineData("(a OR b) AND c", """[AND [OR "a" "b"] "c"]""", true)]
    [InlineData("a = 42", """[= "a" 42]""", false)]
    [InlineData("a = 3.14", """[= "a" 3.14]""", false)]
    [InlineData("a = -0.5", """[= "a" -0.5]""", false)]
    [InlineData("a = 'foo'", """[= "a" "foo"]""", false)]
    [InlineData("a = foo", """[= "a" "foo"]""", false)]
    [InlineData("a = TRUE", """[= "a" ☑]""", false)]
    [InlineData("a = FALSE", """[= "a" ☒]""", false)]
    [InlineData("a = true", """[= "a" ☑]""", false)]
    [InlineData("a = false", """[= "a" ☒]""", false)]
    [InlineData("a = True", """[= "a" ☑]""", false)]
    [InlineData("a = null", """[= "a" ∅]""", false)]
    [InlineData("a = NULL", """[= "a" ∅]""", false)]
    [InlineData("a and b", """[AND "a" "b"]""", true)]
    [InlineData("a or b", """[OR "a" "b"]""", true)]
    [InlineData("not a", @"NOT " + "\"" + @"a" + "\"", true)]
    [InlineData("ANDROID", @"""ANDROID""", true)]
    [InlineData("TRUEBLOOD", @"""TRUEBLOOD""", true)]
    [InlineData("NULLABLE", @"""NULLABLE""", true)]
    [InlineData("a.b.c = 1", """[= "a"."b"."c" 1]""", false)]
    [InlineData("expr.type_map.1.type = 'bar'", """[= "expr"."type_map".1."type" "bar"]""", false)]
    [InlineData("msg.endsWith('world')", @"""msg"".""endsWith""(""world"")", false)]
    [InlineData("foo()", @"""foo""()", false)]
    [InlineData("foo.bar()", @"""foo"".""bar""()", false)]
    [InlineData("foo.bar.baz('a')", @"""foo"".""bar"".""baz""(""a"")", false)]
    [InlineData("name = 'hel*'", """[= "name" "hel*"]""", false)]
    [InlineData("name = '*rld'", """[= "name" "*rld"]""", false)]
    [InlineData("name = '*ello*'", """[= "name" "*ello*"]""", false)]
    [InlineData("msg != \"\\\"\"", @"[!= " + "\"" + @"msg" + "\"" + @" " + "\"\"\"" + @"]", false)] // \" → " (quote)
    [InlineData("msg != \"\\\\\"", @"[!= " + "\"" + @"msg" + "\"" + @" " + "\"" + @"\" + "\"" + @"]", false)] // \\ → \
    [InlineData("msg != \"[ 'hello' ]\"", """[!= "msg" "[ 'hello' ]"]""", false)]
    public void ParseFilter_ReturnsExpected(string input, string expectedToString, bool expectedConstant) {
        var result = Parser.Filter.Parse(input);

        Assert.NotNull(result);
        Assert.Equal(expectedToString, result.ToString());
        Assert.Equal(expectedConstant, result.IsConstant);
    }
}
