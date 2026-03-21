using Xunit;

namespace Schemata.Modeling.Generator.Tests;

public class EntityShould
{
    [Fact]
    public void ParseBasicEntity() {
        var input  = "Entity User {\n  string email_address\n}";
        var result = Parser.Entity.Parse(input);
        Assert.NotNull(result);
        Assert.Equal("User", result.Name);
        Assert.Single(result.Fields);
    }

    [Fact]
    public void ParseEntityWithBases() {
        var input  = "Entity User : BaseEntity, Auditable {\n  string name\n}";
        var result = Parser.Entity.Parse(input);
        Assert.NotNull(result);
        Assert.Equal(2, result.Bases.Length);
        Assert.Equal("BaseEntity", result.Bases[0]);
        Assert.Equal("Auditable", result.Bases[1]);
    }

    [Fact]
    public void ParseEntityWithUse() {
        var input  = "Entity User {\n  Use Entity\n  string name\n}";
        var result = Parser.Entity.Parse(input);
        Assert.NotNull(result);
        Assert.Single(result.Uses);
        Assert.Single(result.Fields);
    }

    [Fact]
    public void ParseEntityWithNestedEnum() {
        var input  = "Entity Post {\n  Enum Status {\n    Draft\n    Published\n  }\n  string title\n}";
        var result = Parser.Entity.Parse(input);
        Assert.NotNull(result);
        Assert.Single(result.Enumerations);
        Assert.Single(result.Fields);
    }

    [Fact]
    public void ParseEntityWithView() {
        var input  = "Entity User {\n  string name\n  Object response {\n    name\n  }\n}";
        var result = Parser.Entity.Parse(input);
        Assert.NotNull(result);
        Assert.Single(result.Views);
        Assert.Single(result.Fields);
    }

    [Fact]
    public void ParseEntityWithPointer() {
        var input  = "Entity Post {\n  long category_id\n  Index category_id [b tree]\n}";
        var result = Parser.Entity.Parse(input);
        Assert.NotNull(result);
        Assert.Single(result.Pointers);
        Assert.Single(result.Fields);
    }

    [Fact]
    public void ParseEntityWithAllMembers() {
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
        Assert.Single(result.Notes);
        Assert.Single(result.Uses);
        Assert.Single(result.Enumerations);
        Assert.Equal(3, result.Fields.Length);
        Assert.Single(result.Pointers);
        Assert.Single(result.Views);
    }

    [Fact]
    public void ParseCaseInsensitive() {
        var input  = "entity User {\n  string name\n}";
        var result = Parser.Entity.Parse(input);
        Assert.NotNull(result);
        Assert.Equal("User", result.Name);
    }
}
