using Schemata.Modeling.Generator.Expressions;
using Xunit;

namespace Schemata.Modeling.Generator.Tests;

public class ViewParserShould
{
    [Fact]
    public void Parse_ViewWithIdentifierFields_BindsNameAndFieldCount() {
        var input  = "Object response {\n  id\n  name\n}";
        var result = Parser.View.Parse(input);
        Assert.NotNull(result);
        Assert.Equal("response", result.Name);
        Assert.Equal(2, result.Fields.Length);
    }

    [Fact]
    public void Parse_ViewWithNote_ReturnsNoteAndField() {
        var input  = "Object response {\n  Note 'A response view'\n  id\n}";
        var result = Parser.View.Parse(input);
        Assert.NotNull(result);
        Assert.Single(result.Notes);
        Assert.Single(result.Fields);
    }

    [Fact]
    public void Parse_LowercaseObjectKeyword_ReturnsView() {
        var input  = "object response {\n  id\n}";
        var result = Parser.View.Parse(input);
        Assert.NotNull(result);
        Assert.Equal("response", result.Name);
    }

    [Fact]
    public void Parse_ViewWithNestedChildren_ReturnsChildField() {
        var input
            = "Object response {\n  Category.response category [omit all] {\n    id = category_id\n  }\n  status\n  title\n  body\n}";
        var result = Parser.View.Parse(input);
        Assert.NotNull(result);
        Assert.Equal(4, result.Fields.Length);
        Assert.Equal("Category.response", result.Fields[0].Type);
        Assert.Equal("category", result.Fields[0].Name);
        Assert.Single(result.Fields[0].Children);
        Assert.Equal("id", result.Fields[0].Children[0].Name);
    }

    [Fact]
    public void Parse_ViewFieldOmitAllOption_ReturnsOmitAllOption() {
        var input  = "Object response {\n  Category.response category [omit all] {\n    id\n  }\n}";
        var result = Parser.View.Parse(input);
        Assert.NotNull(result);
        Assert.Contains(ViewOption.OmitAll, result.Fields[0].Options);
    }

    [Fact]
    public void Parse_UntypedViewField_ReturnsNameWithoutType() {
        var result = Parser.ViewField.Parse("id");
        Assert.NotNull(result);
        Assert.Equal("id", result.Name);
        Assert.Null(result.Type);
        Assert.False(result.Nullable);
    }

    [Fact]
    public void Parse_TypedViewField_ReturnsTypeAndName() {
        var result = Parser.ViewField.Parse("timestamp expiration_date");
        Assert.NotNull(result);
        Assert.Equal("timestamp", result.Type);
        Assert.Equal("expiration_date", result.Name);
    }

    [Fact]
    public void Parse_QualifiedTypeViewField_PreservesQualifiedType() {
        var result = Parser.ViewField.Parse("Category.response category");
        Assert.NotNull(result);
        Assert.Equal("Category.response", result.Type);
        Assert.Equal("category", result.Name);
    }

    [Fact]
    public void Parse_NullableViewField_MarksFieldNullable() {
        var result = Parser.ViewField.Parse("timestamp? modification_date");
        Assert.NotNull(result);
        Assert.Equal("timestamp", result.Type);
        Assert.True(result.Nullable);
    }

    [Fact]
    public void Parse_ViewFieldWithOmitOption_ReturnsOmitOption() {
        var result = Parser.ViewField.Parse("email_address [omit]");
        Assert.NotNull(result);
        Assert.Single(result.Options);
        Assert.Equal(ViewOption.Omit, result.Options[0]);
    }

    [Fact]
    public void Parse_ViewFieldWithReferenceAssignment_ReturnsReference() {
        var result = Parser.ViewField.Parse("category_id [omit] = category.id");
        Assert.NotNull(result);
        var r = Assert.IsType<Reference>(result.Assignment);
        Assert.Equal("category.id", r.QualifiedName);
    }

    [Fact]
    public void Parse_ViewFieldWithFunctionAssignment_ReturnsFunctionCall() {
        var result = Parser.ViewField.Parse("timestamp foo = now()");
        Assert.NotNull(result);
        Assert.Equal("timestamp", result.Type);
        var fn = Assert.IsType<FunctionCall>(result.Assignment);
        Assert.Equal("now", fn.Name);
        Assert.Equal(0, fn.Arguments.Length);
    }

    [Fact]
    public void Parse_ViewFieldWithLiteralAssignment_ReturnsLiteral() {
        var result = Parser.ViewField.Parse("string foo = 'bar'");
        Assert.NotNull(result);
        var lit = Assert.IsType<Literal>(result.Assignment);
        Assert.Equal("bar", lit.Value);
    }

    [Fact]
    public void Parse_ViewFieldWithNestedChildren_ReturnsNestedChild() {
        var input  = "Category.response category [omit all] {\n  id = category_id\n}";
        var result = Parser.ViewField.Parse(input);
        Assert.NotNull(result);
        Assert.Equal("Category.response", result.Type);
        Assert.Equal("category", result.Name);
        Assert.Contains(ViewOption.OmitAll, result.Options);
        Assert.Single(result.Children);
        Assert.Equal("id", result.Children[0].Name);
    }

    [Fact]
    public void Parse_ViewFieldWithNoteAndChildren_ReturnsChildNote() {
        var input  = "User.response user [omit all] {\n  id {\n    Note 'Nested Object field'\n  }\n}";
        var result = Parser.ViewField.Parse(input);
        Assert.NotNull(result);
        Assert.Single(result.Children);
        Assert.Single(result.Children[0].Notes);
        Assert.Equal("Nested Object field", result.Children[0].Notes[0].Text);
    }

    [Fact]
    public void ViewField_WithNoteBeforeOptions_RemainsTyped() {
        var expected = ParseViewField("timestamp expiration_date [omit]");
        var actual   = ParseViewField("timestamp expiration_date /* note */ [omit]");

        Assert.Equal(expected, actual);
        Assert.Equal("timestamp", actual.Type);
        Assert.Equal("expiration_date", actual.Name);
        Assert.False(actual.Nullable);
        Assert.Equal(0, actual.Notes.Length);
        Assert.Contains(ViewOption.Omit, actual.Options);
    }

    [Fact]
    public void ViewField_WithNoteBeforeBody_RemainsUntyped() {
        var expected = ParseViewField("category {\n      id\n    }");
        var actual   = ParseViewField("category /* note */ {\n      id\n    }");

        Assert.Equal(expected, actual);
        Assert.Null(actual.Type);
        Assert.Equal("category", actual.Name);
        Assert.False(actual.Nullable);
        Assert.Equal(0, actual.Notes.Length);
        Assert.Single(actual.Children);
        Assert.Equal("id", actual.Children[0].Name);
    }

    [Fact]
    public void ViewField_WithNoteBeforeAssignment_RemainsTyped() {
        var expected = ParseViewField("timestamp expiration_date = now()");
        var actual   = ParseViewField("timestamp expiration_date /* note */ = now()");

        Assert.Equal(expected, actual);
        Assert.Equal("timestamp", actual.Type);
        Assert.Equal("expiration_date", actual.Name);
        Assert.False(actual.Nullable);
        Assert.Equal(0, actual.Notes.Length);
        Assert.IsType<FunctionCall>(actual.Assignment);
    }

    [Fact]
    public void ViewField_WithNoteBeforeTypedBody_RemainsTyped() {
        var expected = ParseViewField("Category.response category {\n      id\n    }");
        var actual   = ParseViewField("Category.response category /* note */ {\n      id\n    }");

        Assert.Equal(expected, actual);
        Assert.Equal("Category.response", actual.Type);
        Assert.Equal("category", actual.Name);
        Assert.False(actual.Nullable);
        Assert.Equal(0, actual.Notes.Length);
        Assert.Single(actual.Children);
        Assert.Equal("id", actual.Children[0].Name);
    }

    [Fact]
    public void ViewField_WithLineNoteBeforeOptions_RemainsTyped() {
        var expected = ParseViewField("timestamp expiration_date [omit]");
        var actual   = ParseViewField("timestamp expiration_date // note\n    [omit]");

        Assert.Equal(expected, actual);
        Assert.Equal("timestamp", actual.Type);
        Assert.Equal("expiration_date", actual.Name);
        Assert.False(actual.Nullable);
        Assert.Equal(0, actual.Notes.Length);
        Assert.Contains(ViewOption.Omit, actual.Options);
    }

    private static ViewField ParseViewField(string field) {
        var doc = Parser.Document.Parse("Entity Example {\n  Object response {\n    " + field + "\n  }\n}");
        Assert.NotNull(doc);
        Assert.Single(doc.Entities);
        Assert.Single(doc.Entities[0].Views);
        Assert.Single(doc.Entities[0].Views[0].Fields);

        return doc.Entities[0].Views[0].Fields[0];
    }
}
