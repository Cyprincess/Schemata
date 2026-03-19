using Xunit;

namespace Schemata.Modeling.Generator.Tests;

public class CommentShould
{
    [Fact]
    public void SkipLineComments() {
        var input  = "// this is a comment\nEntity User {\n  string name\n}";
        var result = Parser.Entity.Parse(input);
        Assert.Equal("User", result.Name);
    }

    [Fact]
    public void SkipInlineLineComment() {
        var input  = "Entity User { // comment\n  string name\n}";
        var result = Parser.Entity.Parse(input);
        Assert.Single(result.Fields);
    }

    [Fact]
    public void SkipBlockComment() {
        var input  = "/* block comment */Entity User {\n  string name\n}";
        var result = Parser.Entity.Parse(input);
        Assert.Equal("User", result.Name);
    }

    [Fact]
    public void SkipMultilineBlockComment() {
        var input  = "Entity User {\n  /* this\n  is a\n  block comment */\n  string name\n}";
        var result = Parser.Entity.Parse(input);
        Assert.Single(result.Fields);
    }
}
