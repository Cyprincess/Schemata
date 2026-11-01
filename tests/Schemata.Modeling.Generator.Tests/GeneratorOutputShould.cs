using Xunit;

namespace Schemata.Modeling.Generator.Tests;

public class GeneratorOutputShould
{
    [Fact]
    public void Parse_DocumentWithNamespaceAndOneEntity_BindsNamespaceAndEntityName() {
        var input = "Namespace Test.Output\n\nEntity User {\n  string name\n}";
        var doc   = Parser.Document.Parse(input);
        Assert.NotNull(doc);
        Assert.Equal("Test.Output", doc.Namespace);
        Assert.Single(doc.Entities);
        Assert.Equal("User", doc.Entities[0].Name);
    }

    [Fact]
    public void Parse_EntityWithNestedEnumAndTwoFields_BindsEnumerationAndFieldCount() {
        var input = """
            Namespace Test.Output

            Entity Post {
              Enum Status {
                Draft
                Published
              }
              Status status
              string title
            }
            """;
        var doc = Parser.Document.Parse(input);
        Assert.NotNull(doc);
        Assert.Single(doc.Entities);
        var post = doc.Entities[0];
        Assert.Single(post.Enumerations);
        Assert.Equal("Status", post.Enumerations[0].Name);
        Assert.Equal(2, post.Fields.Length);
    }

    [Fact]
    public void Parse_EntityUsingTrait_RegistersTraitAndEntityUseReference() {
        var input = """
            Namespace Test.Output

            Trait Identifier {
              long id [primary key]
            }

            Entity User {
              Use Identifier
              string name
            }
            """;
        var doc = Parser.Document.Parse(input);
        Assert.NotNull(doc);
        Assert.Single(doc.Traits);
        Assert.Single(doc.Entities);
        Assert.Single(doc.Entities[0].Uses);
    }

}
