using Schemata.Modeling.Generator.Expressions;
using Xunit;

namespace Schemata.Modeling.Generator.Tests;

public class EntityParserShould
{
    [Fact]
    public void Parse_BasicEntity() {
        var input  = "Entity User {\n  string email_address\n}";
        var result = Parser.Entity.Parse(input);
        Assert.NotNull(result);
        Assert.Equal("User", result!.Name);
        Assert.Single(result.Fields);
        Assert.Equal("email_address", result.Fields[0].Name);
    }

    [Fact]
    public void Parse_EntityWithBases() {
        var input  = "Entity User : BaseEntity, Auditable {\n  string name\n}";
        var result = Parser.Entity.Parse(input);
        Assert.NotNull(result);
        Assert.Equal(2, result!.Bases.Length);
        Assert.Equal("BaseEntity", result.Bases[0]);
        Assert.Equal("Auditable", result.Bases[1]);
    }

    [Fact]
    public void Parse_EntityWithUses() {
        var input  = "Entity User {\n  Use Entity\n  string name\n}";
        var result = Parser.Entity.Parse(input);
        Assert.NotNull(result);
        Assert.Single(result!.Uses);
        Assert.Single(result.Fields);
    }

    [Fact]
    public void Parse_EntityWithMultipleUses() {
        var input  = "Entity User {\n  Use Identifier, Timestamp\n  string name\n}";
        var result = Parser.Entity.Parse(input);
        Assert.NotNull(result);
        Assert.Single(result!.Uses);
        Assert.Equal(2, result.Uses[0].QualifiedNames.Length);
    }

    [Fact]
    public void Parse_EntityWithNestedEnum() {
        var input  = "Entity Post {\n  Enum Status {\n    Draft\n    Published\n  }\n  string title\n}";
        var result = Parser.Entity.Parse(input);
        Assert.NotNull(result);
        Assert.Single(result!.Enumerations);
        Assert.Equal("Status", result.Enumerations[0].Name);
        Assert.Single(result.Fields);
    }

    [Fact]
    public void Parse_EntityWithView() {
        var input  = "Entity User {\n  string name\n  Object response {\n    name\n  }\n}";
        var result = Parser.Entity.Parse(input);
        Assert.NotNull(result);
        Assert.Single(result!.Views);
        Assert.Equal("response", result.Views[0].Name);
        Assert.Single(result.Fields);
    }

    [Fact]
    public void Parse_EntityWithPointer() {
        var input  = "Entity Post {\n  long category_id\n  Index category_id [b tree]\n}";
        var result = Parser.Entity.Parse(input);
        Assert.NotNull(result);
        Assert.Single(result!.Pointers);
        Assert.Equal("category_id", result.Pointers[0].Columns[0]);
        Assert.Contains(PointerOption.BTree, result.Pointers[0].Options);
        Assert.Single(result.Fields);
    }

    [Fact]
    public void Parse_EntityWithNote() {
        var input  = "Entity User {\n  Note 'A user entity'\n  string name\n}";
        var result = Parser.Entity.Parse(input);
        Assert.NotNull(result);
        Assert.Single(result!.Notes);
        Assert.Equal("A user entity", result.Notes[0].Text);
    }

    [Fact]
    public void Parse_EntityWithAllMembers() {
        var input = """
            Entity Post {
              Note 'A blog post'

              Use Entity

              Enum Status {
                Draft
                Published
              }

              long category_id
              string title
              text body

              Index category_id [b tree]

              Object response {
                status
                title
                body
              }
            }
            """;
        var result = Parser.Entity.Parse(input);
        Assert.NotNull(result);
        Assert.Single(result!.Notes);
        Assert.Single(result.Uses);
        Assert.Single(result.Enumerations);
        Assert.Equal(3, result.Fields.Length);
        Assert.Single(result.Pointers);
        Assert.Single(result.Views);
    }

    [Fact]
    public void Parse_CaseInsensitive() {
        var input  = "entity User {\n  string name\n}";
        var result = Parser.Entity.Parse(input);
        Assert.NotNull(result);
        Assert.Equal("User", result!.Name);
    }

    [Fact]
    public void Parse_EntityWithEmptyBody() {
        var input  = "Entity Empty {}";
        var result = Parser.Entity.Parse(input);
        Assert.NotNull(result);
        Assert.Equal("Empty", result!.Name);
        Assert.Equal(0, result.Fields.Length);
    }
}
