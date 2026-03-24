using System.IO;
using System.Linq;
using Xunit;

namespace Schemata.Modeling.Generator.Tests;

public class GeneratorOutputShould
{
    [Fact]
    public void Parse_MinimalDocument_ProducesDocument() {
        var input = "Namespace Test.Output\n\nEntity User {\n  string name\n}";
        var doc   = Parser.Document.Parse(input);
        Assert.NotNull(doc);
        Assert.Equal("Test.Output", doc.Namespace);
        Assert.Single(doc.Entities);
        Assert.Equal("User", doc.Entities[0].Name);
    }

    [Fact]
    public void Parse_DocumentWithEnumAndEntity() {
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
    public void Parse_DocumentWithTraitAndEntity() {
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

    [Fact]
    public void Parse_Vector1File_ProducesCompleteDocument() {
        var text = File.ReadAllText("vector1.skm");
        var doc  = Parser.Document.Parse(text);
        Assert.NotNull(doc);
        Assert.Equal("DSL.Tests.Vectors", doc.Namespace);
        Assert.Equal(3, doc.Traits.Length);
        Assert.Equal(3, doc.Entities.Length);
        Assert.Equal(0, doc.Enumerations.Length);

        // Verify each entity has the expected member counts
        var user = doc.Entities.First(e => e.Name == "User");
        Assert.Equal(4, user.Fields.Length);
        Assert.Single(user.Views);

        var category = doc.Entities.First(e => e.Name == "Category");
        Assert.Single(category.Fields);
        Assert.Equal(2, category.Views.Length);

        var post = doc.Entities.First(e => e.Name == "Post");
        Assert.Equal(5, post.Fields.Length);
        Assert.Single(post.Enumerations);
        Assert.Single(post.Pointers);
        Assert.Equal(2, post.Views.Length);
    }
}
