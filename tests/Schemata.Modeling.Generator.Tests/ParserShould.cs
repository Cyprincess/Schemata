using System.Linq;
using Schemata.Modeling.Generator.Expressions;
using Xunit;

namespace Schemata.Modeling.Generator.Tests;

public class ParserShould
{
    private static Document ParseVector1() {
        var text = VectorResources.ReadText(VectorResources.Vector1Skm);
        var doc  = Parser.Document.Parse(text);
        Assert.NotNull(doc);
        return doc;
    }

    [Fact]
    public void Parse_Vector1Document_BindsCompleteStructure() {
        var doc = ParseVector1();

        Assert.Equal("DSL.Tests.Vectors", doc.Namespace);
        Assert.Equal(3, doc.Traits.Length);
        Assert.Equal(3, doc.Entities.Length);
        Assert.Equal(0, doc.Enumerations.Length);

        var identifier = doc.Traits.First(t => t.Name == "Identifier");
        Assert.Single(identifier.Notes);
        Assert.Single(identifier.Fields);
        Assert.Equal("long", identifier.Fields[0].Type);
        Assert.Equal("id", identifier.Fields[0].Name);
        Assert.Contains(FieldOption.PrimaryKey, identifier.Fields[0].Options);

        var timestamp = doc.Traits.First(t => t.Name == "Timestamp");
        Assert.Single(timestamp.Notes);
        Assert.Equal(2, timestamp.Fields.Length);
        Assert.True(timestamp.Fields[0].Nullable);
        Assert.Equal("timestamp", timestamp.Fields[0].Type);
        Assert.Equal("creation_date", timestamp.Fields[0].Name);
        Assert.Equal(2, timestamp.Fields[0].Notes.Length);
        Assert.True(timestamp.Fields[1].Nullable);
        Assert.Equal("modification_date", timestamp.Fields[1].Name);

        var entity = doc.Traits.First(t => t.Name == "Entity");
        Assert.Single(entity.Uses);
        Assert.Equal(2, entity.Uses[0].QualifiedNames.Length);
        Assert.Contains("Identifier", entity.Uses[0].QualifiedNames);
        Assert.Contains("Timestamp", entity.Uses[0].QualifiedNames);

        var user = doc.Entities.First(e => e.Name == "User");
        Assert.Single(user.Notes);
        Assert.Single(user.Uses);
        Assert.Equal("Entity", user.Uses[0].QualifiedNames[0]);
        Assert.Equal(4, user.Fields.Length);
        Assert.Equal("email_address", user.Fields[0].Name);
        Assert.Equal("string", user.Fields[0].Type);
        Assert.Contains(FieldOption.BTree, user.Fields[0].Options);
        Assert.Equal("phone_number", user.Fields[1].Name);
        Assert.Contains(FieldOption.BTree, user.Fields[1].Options);
        Assert.Equal("password", user.Fields[2].Name);
        Assert.Equal("nickname", user.Fields[3].Name);
        Assert.Single(user.Views);

        var response = user.Views[0];
        Assert.Equal("response", response.Name);
        // Body-context parser: "nickname email_address [omit]" becomes typed field
        // (type=nickname, name=email_address) because [omit] is a continuation token.
        Assert.Equal(5, response.Fields.Length);
        Assert.Equal("id", response.Fields[0].Name);
        Assert.Equal(0, response.Fields[0].Options.Length);
        Assert.Equal("email_address", response.Fields[1].Name);
        Assert.Equal("nickname", response.Fields[1].Type);
        Assert.Contains(ViewOption.Omit, response.Fields[1].Options);
        Assert.Equal("obfuscated_email_address", response.Fields[2].Name);
        Assert.Contains(ViewOption.Omit, response.Fields[2].Options);
        Assert.NotNull(response.Fields[2].Assignment);
        var obfuscate = Assert.IsType<FunctionCall>(response.Fields[2].Assignment);
        Assert.Equal("obfuscate", obfuscate.Name);
        Assert.Single(obfuscate.Arguments);
        Assert.Equal("phone_number", response.Fields[3].Name);
        Assert.Contains(ViewOption.Omit, response.Fields[3].Options);
        Assert.Equal("obfuscated_phone_number", response.Fields[4].Name);
        Assert.Contains(ViewOption.Omit, response.Fields[4].Options);

        var category = doc.Entities.First(e => e.Name == "Category");
        Assert.Single(category.Notes);
        Assert.Single(category.Uses);
        Assert.Single(category.Fields);
        Assert.Equal("string", category.Fields[0].Type);
        Assert.Equal("Name", category.Fields[0].Name);
        Assert.Contains(FieldOption.Required, category.Fields[0].Options);
        Assert.Equal(2, category.Views.Length);

        var categoryRequest = category.Views.First(v => v.Name == "request");
        Assert.Single(categoryRequest.Fields);
        Assert.Equal("name", categoryRequest.Fields[0].Name);

        var categoryResponse = category.Views.First(v => v.Name == "response");
        Assert.Equal(3, categoryResponse.Fields.Length);
        Assert.Equal("id", categoryResponse.Fields[0].Name);
        Assert.Equal("name", categoryResponse.Fields[1].Name);
        Assert.Equal("expiration_date", categoryResponse.Fields[2].Name);
        Assert.Equal("timestamp", categoryResponse.Fields[2].Type);
        Assert.NotNull(categoryResponse.Fields[2].Assignment);
        var now = Assert.IsType<FunctionCall>(categoryResponse.Fields[2].Assignment);
        Assert.Equal("now", now.Name);
        Assert.Equal(0, now.Arguments.Length);

        var post = doc.Entities.First(e => e.Name == "Post");
        Assert.Single(post.Uses);
        Assert.Single(post.Enumerations);
        Assert.Single(post.Pointers);
        Assert.Equal(2, post.Views.Length);
        Assert.Equal(5, post.Fields.Length);

        var status = post.Enumerations[0];
        Assert.Equal("Status", status.Name);
        Assert.Equal(2, status.Values.Length);
        Assert.Equal("Draft", status.Values[0].Name);
        Assert.Single(status.Values[0].Notes);
        Assert.Equal("Published", status.Values[1].Name);
        Assert.Equal(0, status.Values[1].Notes.Length);

        Assert.Equal("category_id", post.Fields[0].Name);
        Assert.Equal("long", post.Fields[0].Type);
        Assert.Equal("user_id", post.Fields[1].Name);
        Assert.Contains(FieldOption.BTree, post.Fields[1].Options);
        Assert.Equal("status", post.Fields[2].Name);
        Assert.Equal("Status", post.Fields[2].Type);
        Assert.Single(post.Fields[2].Properties);
        Assert.Equal("default", post.Fields[2].Properties[0].Key);
        var defaultValue = Assert.IsType<Literal>(post.Fields[2].Properties[0].Value);
        Assert.Equal("Published", defaultValue.Value);
        Assert.Equal("title", post.Fields[3].Name);
        Assert.Single(post.Fields[3].Notes);
        Assert.Equal("Title of the post", post.Fields[3].Notes[0].Text);
        Assert.Equal("body", post.Fields[4].Name);
        Assert.Equal("text", post.Fields[4].Type);

        var index = post.Pointers[0];
        Assert.Single(index.Columns);
        Assert.Equal("category_id", index.Columns[0]);
        Assert.Contains(PointerOption.BTree, index.Options);

        var postRequest = post.Views.First(v => v.Name == "request");
        Assert.Equal(5, postRequest.Fields.Length);
        Assert.Equal("category", postRequest.Fields[0].Name);
        Assert.Equal("Category.response", postRequest.Fields[0].Type);
        Assert.Contains(ViewOption.OmitAll, postRequest.Fields[0].Options);
        Assert.Single(postRequest.Fields[0].Notes);
        Assert.Single(postRequest.Fields[0].Children);
        Assert.Equal("id", postRequest.Fields[0].Children[0].Name);
        var categoryId = postRequest.Fields.First(f => f.Name == "category_id");
        Assert.Contains(ViewOption.Omit, categoryId.Options);
        var reference = Assert.IsType<Reference>(categoryId.Assignment);
        Assert.Equal("category.id", reference.QualifiedName);

        var postResponse = post.Views.First(v => v.Name == "response");
        Assert.Equal(5, postResponse.Fields.Length);
        var nestedUser = postResponse.Fields.First(f => f.Name == "user");
        Assert.Equal("User.response", nestedUser.Type);
        Assert.Contains(ViewOption.OmitAll, nestedUser.Options);
        Assert.Single(nestedUser.Children);
        Assert.Equal("id", nestedUser.Children[0].Name);
        Assert.Single(nestedUser.Children[0].Notes);
    }
}
