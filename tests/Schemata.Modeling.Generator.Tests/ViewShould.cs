using Xunit;

namespace Schemata.Modeling.Generator.Tests;

public class ViewShould
{
    [Fact]
    public void ParseBasicView() {
        var input  = "Object response {\n  id\n  name\n}";
        var result = Parser.View.Parse(input);
        Assert.Equal("response", result.Name);
        Assert.Equal(2, result.Fields.Length);
    }

    [Fact]
    public void ParseViewWithNote() {
        var input  = "Object response {\n  Note 'A response view'\n  id\n}";
        var result = Parser.View.Parse(input);
        Assert.Single(result.Notes);
        Assert.Single(result.Fields);
    }

    [Fact]
    public void ParseCaseInsensitive() {
        var input  = "object response {\n  id\n}";
        var result = Parser.View.Parse(input);
        Assert.Equal("response", result.Name);
    }

    [Fact]
    public void ParseComplexView() {
        var input = @"Object response {
  Category.response category [omit all] {
    id = category_id
  }
  status
  title
  body
}";
        var result = Parser.View.Parse(input);
        Assert.Equal(4, result.Fields.Length);
        Assert.Equal("Category.response", result.Fields[0].Type);
        Assert.Single(result.Fields[0].Children);
    }
}
