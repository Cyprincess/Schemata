using Schemata.Modeling.Generator.Expressions;
using Xunit;

namespace Schemata.Modeling.Generator.Tests;

public class FieldParserShould
{
    [Fact]
    public void Parse_BasicField() {
        var result = Parser.Field.Parse("string email_address");
        Assert.NotNull(result);
        Assert.Equal("string", result!.Type);
        Assert.Equal("email_address", result.Name);
        Assert.False(result.Nullable);
    }

    [Fact]
    public void Parse_NullableField() {
        var result = Parser.Field.Parse("timestamp? creation_date");
        Assert.NotNull(result);
        Assert.Equal("timestamp", result!.Type);
        Assert.True(result.Nullable);
        Assert.Equal("creation_date", result.Name);
    }

    [Fact]
    public void Parse_PrimaryKeyOption() {
        var result = Parser.Field.Parse("long id [primary key]");
        Assert.NotNull(result);
        Assert.Single(result!.Options);
        Assert.Equal(FieldOption.PrimaryKey, result.Options[0]);
    }

    [Fact]
    public void Parse_MultipleOptions() {
        var result = Parser.Field.Parse("long id [primary key, auto increment]");
        Assert.NotNull(result);
        Assert.Equal(2, result!.Options.Length);
        Assert.Contains(FieldOption.PrimaryKey, result.Options);
        Assert.Contains(FieldOption.AutoIncrement, result.Options);
    }

    [Fact]
    public void Parse_RequiredOption() {
        var result = Parser.Field.Parse("string name [required]");
        Assert.NotNull(result);
        Assert.Single(result!.Options);
        Assert.Equal(FieldOption.Required, result.Options[0]);
    }

    [Fact]
    public void Parse_NotNullMapsToRequired() {
        var result = Parser.Field.Parse("string name [not null]");
        Assert.NotNull(result);
        Assert.Single(result!.Options);
        Assert.Equal(FieldOption.Required, result.Options[0]);
    }

    [Fact]
    public void Parse_UniqueOption() {
        var result = Parser.Field.Parse("string email [unique]");
        Assert.NotNull(result);
        Assert.Single(result!.Options);
        Assert.Equal(FieldOption.Unique, result.Options[0]);
    }

    [Fact]
    public void Parse_BTreeOption() {
        var result = Parser.Field.Parse("string email [b tree]");
        Assert.NotNull(result);
        Assert.Single(result!.Options);
        Assert.Equal(FieldOption.BTree, result.Options[0]);
    }

    [Fact]
    public void Parse_HashOption() {
        var result = Parser.Field.Parse("string email [hash]");
        Assert.NotNull(result);
        Assert.Single(result!.Options);
        Assert.Equal(FieldOption.Hash, result.Options[0]);
    }

    [Fact]
    public void Parse_DefaultProperty() {
        var input  = "Status status {\n  Default 'Published'\n}";
        var result = Parser.Field.Parse(input);
        Assert.NotNull(result);
        Assert.Single(result!.Properties);
        Assert.Equal("Default", result.Properties[0].Key);
        var lit = Assert.IsType<Literal>(result.Properties[0].Value);
        Assert.Equal("Published", lit.Value);
    }

    [Fact]
    public void Parse_LengthProperty() {
        var input  = "string password [required] {\n  Length 128\n}";
        var result = Parser.Field.Parse(input);
        Assert.NotNull(result);
        Assert.Single(result!.Options);
        Assert.Single(result.Properties);
        Assert.Equal("Length", result.Properties[0].Key);
        var num = Assert.IsType<NumberLiteral>(result.Properties[0].Value);
        Assert.Equal("128", num.Raw);
    }

    [Fact]
    public void Parse_FieldWithSingleNote() {
        var input  = "string title {\n  Note 'Title of the post'\n}";
        var result = Parser.Field.Parse(input);
        Assert.NotNull(result);
        Assert.Single(result!.Notes);
        Assert.Equal("Title of the post", result.Notes[0].Text);
    }

    [Fact]
    public void Parse_FieldWithMultipleNotes() {
        var input  = "timestamp? creation_date {\n  Note 'First note'\n  Note 'Second note'\n}";
        var result = Parser.Field.Parse(input);
        Assert.NotNull(result);
        Assert.Equal(2, result!.Notes.Length);
    }

    [Fact]
    public void Parse_QualifiedTypeName() {
        var result = Parser.Field.Parse("Post.Status status");
        Assert.NotNull(result);
        Assert.Equal("Post.Status", result!.Type);
        Assert.Equal("status", result.Name);
    }

    [Fact]
    public void Parse_OptionsAndProperties() {
        var input  = "string password [required] {\n  Length 128\n}";
        var result = Parser.Field.Parse(input);
        Assert.NotNull(result);
        Assert.Single(result!.Options);
        Assert.Single(result.Properties);
    }

    [Fact]
    public void Parse_NoteAndProperty() {
        var input  = "Status status {\n  Note 'The status field'\n  Default 'Published'\n}";
        var result = Parser.Field.Parse(input);
        Assert.NotNull(result);
        Assert.Single(result!.Notes);
        Assert.Single(result.Properties);
    }
}
