using Schemata.Modeling.Generator.Expressions;
using Xunit;

namespace Schemata.Modeling.Generator.Tests;

public class ViewFieldShould
{
    [Fact]
    public void ParseUntypedField() {
        var result = Parser.ViewField.Parse("id");
        Assert.NotNull(result);
        Assert.Equal("id", result.Name);
        Assert.Null(result.Type);
    }

    [Fact]
    public void ParseTypedField() {
        var result = Parser.ViewField.Parse("timestamp expiration_date");
        Assert.NotNull(result);
        Assert.Equal("timestamp", result.Type);
        Assert.Equal("expiration_date", result.Name);
    }

    [Fact]
    public void ParseQualifiedType() {
        var result = Parser.ViewField.Parse("Category.response category");
        Assert.NotNull(result);
        Assert.Equal("Category.response", result.Type);
        Assert.Equal("category", result.Name);
    }

    [Fact]
    public void ParseNullable() {
        var result = Parser.ViewField.Parse("timestamp? modification_date");
        Assert.NotNull(result);
        Assert.Equal("timestamp", result.Type);
        Assert.True(result.Nullable);
    }

    [Fact]
    public void ParseWithViewOptions() {
        var result = Parser.ViewField.Parse("email_address [omit]");
        Assert.NotNull(result);
        Assert.Single(result.Options);
        Assert.Equal(ViewOption.Omit, result.Options[0]);
    }

    [Fact]
    public void ParseWithAssignmentRef() {
        var result = Parser.ViewField.Parse("category_id [omit] = category.id");
        Assert.NotNull(result);
        var r = Assert.IsType<Reference>(result.Assignment);
        Assert.Equal("category.id", r.QualifiedName);
    }

    [Fact]
    public void ParseWithAssignmentFunction() {
        var result = Parser.ViewField.Parse("timestamp foo = now()");
        Assert.NotNull(result);
        Assert.Equal("timestamp", result.Type);
        Assert.IsType<FunctionCall>(result.Assignment);
    }

    [Fact]
    public void ParseWithAssignmentLiteral() {
        var result = Parser.ViewField.Parse("string foo = 'bar'");
        Assert.NotNull(result);
        Assert.IsType<Literal>(result.Assignment);
    }

    [Fact]
    public void ParseNestedChildren() {
        var input  = "Category.response category [omit all] {\n  id = category_id\n}";
        var result = Parser.ViewField.Parse(input);
        Assert.NotNull(result);
        Assert.Equal("Category.response", result.Type);
        Assert.Equal("category", result.Name);
        Assert.Single(result.Children);
        Assert.Equal("id", result.Children[0].Name);
    }

    [Fact]
    public void ParseWithNoteAndChildren() {
        var input  = "User.response user [omit all] {\n  id {\n    Note 'Nested Object field'\n  }\n}";
        var result = Parser.ViewField.Parse(input);
        Assert.NotNull(result);
        Assert.Single(result.Children);
        Assert.Single(result.Children[0].Notes);
    }
}
