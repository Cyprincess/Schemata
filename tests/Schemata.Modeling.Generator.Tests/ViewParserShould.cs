using Schemata.Modeling.Generator.Expressions;
using Xunit;

namespace Schemata.Modeling.Generator.Tests;

public class ViewParserShould
{
    [Fact]
    public void Parse_BasicView() {
        var input  = "Object response {\n  id\n  name\n}";
        var result = Parser.View.Parse(input);
        Assert.NotNull(result);
        Assert.Equal("response", result!.Name);
        Assert.Equal(2, result.Fields.Length);
    }

    [Fact]
    public void Parse_ViewWithNote() {
        var input  = "Object response {\n  Note 'A response view'\n  id\n}";
        var result = Parser.View.Parse(input);
        Assert.NotNull(result);
        Assert.Single(result!.Notes);
        Assert.Single(result.Fields);
    }

    [Fact]
    public void Parse_CaseInsensitive() {
        var input  = "object response {\n  id\n}";
        var result = Parser.View.Parse(input);
        Assert.NotNull(result);
        Assert.Equal("response", result!.Name);
    }

    [Fact]
    public void Parse_ViewWithNestedChildren() {
        var input       = "Object response {\n  Category.response category [omit all] {\n    id = category_id\n  }\n  status\n  title\n  body\n}";
        var result = Parser.View.Parse(input);
        Assert.NotNull(result);
        Assert.Equal(4, result!.Fields.Length);
        Assert.Equal("Category.response", result.Fields[0].Type);
        Assert.Equal("category", result.Fields[0].Name);
        Assert.Single(result.Fields[0].Children);
        Assert.Equal("id", result.Fields[0].Children[0].Name);
    }

    [Fact]
    public void Parse_ViewFieldOmitAllOption() {
        var input  = "Object response {\n  Category.response category [omit all] {\n    id\n  }\n}";
        var result = Parser.View.Parse(input);
        Assert.NotNull(result);
        Assert.Contains(ViewOption.OmitAll, result!.Fields[0].Options);
    }

    [Fact]
    public void Parse_UntypedViewField() {
        var result = Parser.ViewField.Parse("id");
        Assert.NotNull(result);
        Assert.Equal("id", result!.Name);
        Assert.Null(result.Type);
        Assert.False(result.Nullable);
    }

    [Fact]
    public void Parse_TypedViewField() {
        var result = Parser.ViewField.Parse("timestamp expiration_date");
        Assert.NotNull(result);
        Assert.Equal("timestamp", result!.Type);
        Assert.Equal("expiration_date", result.Name);
    }

    [Fact]
    public void Parse_QualifiedTypeViewField() {
        var result = Parser.ViewField.Parse("Category.response category");
        Assert.NotNull(result);
        Assert.Equal("Category.response", result!.Type);
        Assert.Equal("category", result.Name);
    }

    [Fact]
    public void Parse_NullableViewField() {
        var result = Parser.ViewField.Parse("timestamp? modification_date");
        Assert.NotNull(result);
        Assert.Equal("timestamp", result!.Type);
        Assert.True(result.Nullable);
    }

    [Fact]
    public void Parse_ViewFieldWithOmitOption() {
        var result = Parser.ViewField.Parse("email_address [omit]");
        Assert.NotNull(result);
        Assert.Single(result!.Options);
        Assert.Equal(ViewOption.Omit, result.Options[0]);
    }

    [Fact]
    public void Parse_ViewFieldWithReferenceAssignment() {
        var result = Parser.ViewField.Parse("category_id [omit] = category.id");
        Assert.NotNull(result);
        var r = Assert.IsType<Reference>(result!.Assignment);
        Assert.Equal("category.id", r.QualifiedName);
    }

    [Fact]
    public void Parse_ViewFieldWithFunctionAssignment() {
        var result = Parser.ViewField.Parse("timestamp foo = now()");
        Assert.NotNull(result);
        Assert.Equal("timestamp", result!.Type);
        var fn = Assert.IsType<FunctionCall>(result.Assignment);
        Assert.Equal("now", fn.Name);
        Assert.Equal(0, fn.Arguments.Length);
    }

    [Fact]
    public void Parse_ViewFieldWithLiteralAssignment() {
        var result = Parser.ViewField.Parse("string foo = 'bar'");
        Assert.NotNull(result);
        var lit = Assert.IsType<Literal>(result!.Assignment);
        Assert.Equal("bar", lit.Value);
    }

    [Fact]
    public void Parse_ViewFieldWithNestedChildren() {
        var input  = "Category.response category [omit all] {\n  id = category_id\n}";
        var result = Parser.ViewField.Parse(input);
        Assert.NotNull(result);
        Assert.Equal("Category.response", result!.Type);
        Assert.Equal("category", result.Name);
        Assert.Contains(ViewOption.OmitAll, result.Options);
        Assert.Single(result.Children);
        Assert.Equal("id", result.Children[0].Name);
    }

    [Fact]
    public void Parse_ViewFieldWithNoteAndChildren() {
        var input  = "User.response user [omit all] {\n  id {\n    Note 'Nested Object field'\n  }\n}";
        var result = Parser.ViewField.Parse(input);
        Assert.NotNull(result);
        Assert.Single(result!.Children);
        Assert.Single(result.Children[0].Notes);
        Assert.Equal("Nested Object field", result.Children[0].Notes[0].Text);
    }
}
