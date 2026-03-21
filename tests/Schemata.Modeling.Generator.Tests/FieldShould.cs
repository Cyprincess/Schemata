using Schemata.Modeling.Generator.Expressions;
using Xunit;

namespace Schemata.Modeling.Generator.Tests;

public class FieldShould
{
    [Fact]
    public void ParseBasicField() {
        var result = Parser.Field.Parse("string email_address");
        Assert.NotNull(result);
        Assert.Equal("string", result.Type);
        Assert.Equal("email_address", result.Name);
        Assert.False(result.Nullable);
    }

    [Fact]
    public void ParseNullableField() {
        var result = Parser.Field.Parse("timestamp? creation_date");
        Assert.NotNull(result);
        Assert.Equal("timestamp", result.Type);
        Assert.True(result.Nullable);
        Assert.Equal("creation_date", result.Name);
    }

    [Fact]
    public void ParseFieldWithOptions() {
        var result = Parser.Field.Parse("long id [primary key]");
        Assert.NotNull(result);
        Assert.Single(result.Options);
        Assert.Equal(FieldOption.PrimaryKey, result.Options[0]);
    }

    [Fact]
    public void ParseFieldWithMultipleOptions() {
        var result = Parser.Field.Parse("long id [primary key, auto increment]");
        Assert.NotNull(result);
        Assert.Equal(2, result.Options.Length);
    }

    [Fact]
    public void ParseFieldWithProperties() {
        var input  = "Status status {\n  Default 'Published'\n}";
        var result = Parser.Field.Parse(input);
        Assert.NotNull(result);
        Assert.Single(result.Properties);
        Assert.Equal("Default", result.Properties[0].Key);
    }

    [Fact]
    public void ParseFieldWithNotes() {
        var input  = "string title {\n  Note 'Title of the post'\n}";
        var result = Parser.Field.Parse(input);
        Assert.NotNull(result);
        Assert.Single(result.Notes);
        Assert.Equal("Title of the post", result.Notes[0].Text);
    }

    [Fact]
    public void ParseFieldWithOptionsAndProperties() {
        var input  = "string password [required] {\n  Length 128\n}";
        var result = Parser.Field.Parse(input);
        Assert.NotNull(result);
        Assert.Single(result.Options);
        Assert.Single(result.Properties);
    }

    [Fact]
    public void ParseQualifiedType() {
        var result = Parser.Field.Parse("Post.Status status");
        Assert.NotNull(result);
        Assert.Equal("Post.Status", result.Type);
        Assert.Equal("status", result.Name);
    }
}
