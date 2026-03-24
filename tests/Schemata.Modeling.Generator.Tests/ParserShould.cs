using System.IO;
using System.Linq;
using Schemata.Modeling.Generator.Expressions;
using Xunit;

namespace Schemata.Modeling.Generator.Tests;

public class ParserShould
{
    private static Document ParseVector1() {
        var text = File.ReadAllText("vector1.skm");
        var doc  = Parser.Document.Parse(text);
        Assert.NotNull(doc);
        return doc;
    }

    [Fact]
    public void Parse_Namespace() {
        var doc = ParseVector1();
        Assert.Equal("DSL.Tests.Vectors", doc.Namespace);
    }

    [Fact]
    public void Parse_ThreeTraits() {
        var doc = ParseVector1();
        Assert.Equal(3, doc.Traits.Length);
        Assert.Contains(doc.Traits, t => t.Name == "Identifier");
        Assert.Contains(doc.Traits, t => t.Name == "Timestamp");
        Assert.Contains(doc.Traits, t => t.Name == "Entity");
    }

    [Fact]
    public void Parse_ThreeEntities() {
        var doc = ParseVector1();
        Assert.Equal(3, doc.Entities.Length);
        Assert.Contains(doc.Entities, e => e.Name == "User");
        Assert.Contains(doc.Entities, e => e.Name == "Category");
        Assert.Contains(doc.Entities, e => e.Name == "Post");
    }

    [Fact]
    public void Parse_NoTopLevelEnumerations() {
        var doc = ParseVector1();
        Assert.Equal(0, doc.Enumerations.Length);
    }

    [Fact]
    public void Parse_IdentifierTrait_FieldAndNote() {
        var doc   = ParseVector1();
        var trait = doc.Traits.First(t => t.Name == "Identifier");
        Assert.Single(trait.Notes);
        Assert.Single(trait.Fields);
        Assert.Equal("long", trait.Fields[0].Type);
        Assert.Equal("id", trait.Fields[0].Name);
        Assert.Contains(FieldOption.PrimaryKey, trait.Fields[0].Options);
    }

    [Fact]
    public void Parse_TimestampTrait_NullableFields() {
        var doc   = ParseVector1();
        var trait = doc.Traits.First(t => t.Name == "Timestamp");
        Assert.Single(trait.Notes);
        Assert.Equal(2, trait.Fields.Length);
        Assert.True(trait.Fields[0].Nullable);
        Assert.Equal("timestamp", trait.Fields[0].Type);
        Assert.Equal("creation_date", trait.Fields[0].Name);
        Assert.True(trait.Fields[1].Nullable);
        Assert.Equal("modification_date", trait.Fields[1].Name);
    }

    [Fact]
    public void Parse_TimestampTrait_CreationDateHasTwoNotes() {
        var doc   = ParseVector1();
        var trait = doc.Traits.First(t => t.Name == "Timestamp");
        Assert.Equal(2, trait.Fields[0].Notes.Length);
    }

    [Fact]
    public void Parse_EntityTrait_UsesBothIdentifierAndTimestamp() {
        var doc   = ParseVector1();
        var trait = doc.Traits.First(t => t.Name == "Entity");
        Assert.Single(trait.Uses);
        Assert.Equal(2, trait.Uses[0].QualifiedNames.Length);
        Assert.Contains("Identifier", trait.Uses[0].QualifiedNames);
        Assert.Contains("Timestamp", trait.Uses[0].QualifiedNames);
    }

    [Fact]
    public void Parse_UserEntity_Structure() {
        var doc  = ParseVector1();
        var user = doc.Entities.First(e => e.Name == "User");
        Assert.Single(user.Notes);
        Assert.Single(user.Uses);
        Assert.Equal("Entity", user.Uses[0].QualifiedNames[0]);
        Assert.Equal(4, user.Fields.Length);
        Assert.Single(user.Views);
    }

    [Fact]
    public void Parse_UserEntity_Fields() {
        var doc  = ParseVector1();
        var user = doc.Entities.First(e => e.Name == "User");
        Assert.Equal("email_address", user.Fields[0].Name);
        Assert.Equal("string", user.Fields[0].Type);
        Assert.Contains(FieldOption.BTree, user.Fields[0].Options);
        Assert.Equal("phone_number", user.Fields[1].Name);
        Assert.Contains(FieldOption.BTree, user.Fields[1].Options);
        Assert.Equal("password", user.Fields[2].Name);
        Assert.Equal("nickname", user.Fields[3].Name);
    }

    [Fact]
    public void Parse_UserEntity_ResponseViewFieldCount() {
        var doc      = ParseVector1();
        var user     = doc.Entities.First(e => e.Name == "User");
        var response = user.Views[0];
        Assert.Equal("response", response.Name);
        // Body-context parser: "nickname email_address [omit]" becomes typed field
        // (type=nickname, name=email_address) because [omit] is a continuation token.
        Assert.Equal(5, response.Fields.Length);
    }

    [Fact]
    public void Parse_UserEntity_ResponseViewFieldNames() {
        var doc      = ParseVector1();
        var user     = doc.Entities.First(e => e.Name == "User");
        var response = user.Views[0];
        Assert.Equal("id", response.Fields[0].Name);
        Assert.Equal("email_address", response.Fields[1].Name);
        Assert.Equal("nickname", response.Fields[1].Type);
        Assert.Equal("obfuscated_email_address", response.Fields[2].Name);
        Assert.Equal("phone_number", response.Fields[3].Name);
        Assert.Equal("obfuscated_phone_number", response.Fields[4].Name);
    }

    [Fact]
    public void Parse_UserEntity_FunctionCallAssignment() {
        var doc        = ParseVector1();
        var user       = doc.Entities.First(e => e.Name == "User");
        var response   = user.Views[0];
        var obfuscated = response.Fields.First(f => f.Name == "obfuscated_email_address");
        Assert.NotNull(obfuscated.Assignment);
        var fn = Assert.IsType<FunctionCall>(obfuscated.Assignment);
        Assert.Equal("obfuscate", fn.Name);
        Assert.Single(fn.Arguments);
    }

    [Fact]
    public void Parse_UserEntity_OmitOptions() {
        var doc      = ParseVector1();
        var user     = doc.Entities.First(e => e.Name == "User");
        var response = user.Views[0];
        Assert.Equal(0, response.Fields[0].Options.Length);
        Assert.Contains(ViewOption.Omit, response.Fields[1].Options);
        Assert.Contains(ViewOption.Omit, response.Fields[2].Options);
        Assert.Contains(ViewOption.Omit, response.Fields[3].Options);
        Assert.Contains(ViewOption.Omit, response.Fields[4].Options);
    }

    [Fact]
    public void Parse_CategoryEntity_Structure() {
        var doc      = ParseVector1();
        var category = doc.Entities.First(e => e.Name == "Category");
        Assert.Single(category.Notes);
        Assert.Single(category.Uses);
        Assert.Single(category.Fields);
        Assert.Equal(2, category.Views.Length);
    }

    [Fact]
    public void Parse_CategoryEntity_NameFieldRequiredOption() {
        var doc   = ParseVector1();
        var cat   = doc.Entities.First(e => e.Name == "Category");
        var field = cat.Fields[0];
        Assert.Equal("string", field.Type);
        Assert.Equal("Name", field.Name);
        Assert.Contains(FieldOption.Required, field.Options);
    }

    [Fact]
    public void Parse_CategoryEntity_RequestView() {
        var doc     = ParseVector1();
        var cat     = doc.Entities.First(e => e.Name == "Category");
        var request = cat.Views.First(v => v.Name == "request");
        Assert.Single(request.Fields);
        Assert.Equal("name", request.Fields[0].Name);
    }

    [Fact]
    public void Parse_CategoryEntity_ResponseView() {
        var doc      = ParseVector1();
        var cat      = doc.Entities.First(e => e.Name == "Category");
        var response = cat.Views.First(v => v.Name == "response");
        Assert.Equal(3, response.Fields.Length);
        Assert.Equal("id", response.Fields[0].Name);
        Assert.Equal("name", response.Fields[1].Name);
        Assert.Equal("expiration_date", response.Fields[2].Name);
    }

    [Fact]
    public void Parse_CategoryEntity_NowFunctionCall() {
        var doc      = ParseVector1();
        var cat      = doc.Entities.First(e => e.Name == "Category");
        var response = cat.Views.First(v => v.Name == "response");
        var expDate  = response.Fields.First(f => f.Name == "expiration_date");
        Assert.Equal("timestamp", expDate.Type);
        Assert.NotNull(expDate.Assignment);
        var fn = Assert.IsType<FunctionCall>(expDate.Assignment);
        Assert.Equal("now", fn.Name);
        Assert.Equal(0, fn.Arguments.Length);
    }

    [Fact]
    public void Parse_PostEntity_Structure() {
        var doc  = ParseVector1();
        var post = doc.Entities.First(e => e.Name == "Post");
        Assert.Single(post.Uses);
        Assert.Single(post.Enumerations);
        Assert.Single(post.Pointers);
        Assert.Equal(2, post.Views.Length);
        Assert.Equal(5, post.Fields.Length);
    }

    [Fact]
    public void Parse_PostEntity_StatusEnum() {
        var doc    = ParseVector1();
        var post   = doc.Entities.First(e => e.Name == "Post");
        var status = post.Enumerations[0];
        Assert.Equal("Status", status.Name);
        Assert.Equal(2, status.Values.Length);
        Assert.Equal("Draft", status.Values[0].Name);
        Assert.Equal("Published", status.Values[1].Name);
        Assert.Single(status.Values[0].Notes);
        Assert.Equal(0, status.Values[1].Notes.Length);
    }

    [Fact]
    public void Parse_PostEntity_Fields() {
        var doc  = ParseVector1();
        var post = doc.Entities.First(e => e.Name == "Post");
        Assert.Equal("category_id", post.Fields[0].Name);
        Assert.Equal("long", post.Fields[0].Type);
        Assert.Equal("user_id", post.Fields[1].Name);
        Assert.Contains(FieldOption.BTree, post.Fields[1].Options);
        Assert.Equal("status", post.Fields[2].Name);
        Assert.Equal("Status", post.Fields[2].Type);
        Assert.Equal("title", post.Fields[3].Name);
        Assert.Equal("body", post.Fields[4].Name);
        Assert.Equal("text", post.Fields[4].Type);
    }

    [Fact]
    public void Parse_PostEntity_StatusFieldDefault() {
        var doc    = ParseVector1();
        var post   = doc.Entities.First(e => e.Name == "Post");
        var status = post.Fields.First(f => f.Name == "status");
        Assert.Single(status.Properties);
        Assert.Equal("default", status.Properties[0].Key);
        var lit = Assert.IsType<Literal>(status.Properties[0].Value);
        Assert.Equal("Published", lit.Value);
    }

    [Fact]
    public void Parse_PostEntity_TitleNote() {
        var doc   = ParseVector1();
        var post  = doc.Entities.First(e => e.Name == "Post");
        var title = post.Fields.First(f => f.Name == "title");
        Assert.Single(title.Notes);
        Assert.Equal("Title of the post", title.Notes[0].Text);
    }

    [Fact]
    public void Parse_PostEntity_Index() {
        var doc  = ParseVector1();
        var post = doc.Entities.First(e => e.Name == "Post");
        var idx  = post.Pointers[0];
        Assert.Single(idx.Columns);
        Assert.Equal("category_id", idx.Columns[0]);
        Assert.Contains(PointerOption.BTree, idx.Options);
    }

    [Fact]
    public void Parse_PostEntity_RequestView() {
        var doc     = ParseVector1();
        var post    = doc.Entities.First(e => e.Name == "Post");
        var request = post.Views.First(v => v.Name == "request");
        Assert.Equal(5, request.Fields.Length);
        Assert.Equal("category", request.Fields[0].Name);
        Assert.Equal("Category.response", request.Fields[0].Type);
        Assert.Contains(ViewOption.OmitAll, request.Fields[0].Options);
    }

    [Fact]
    public void Parse_PostEntity_RequestCategoryChildren() {
        var doc      = ParseVector1();
        var post     = doc.Entities.First(e => e.Name == "Post");
        var request  = post.Views.First(v => v.Name == "request");
        var category = request.Fields.First(f => f.Name == "category");
        Assert.Single(category.Notes);
        Assert.Single(category.Children);
        Assert.Equal("id", category.Children[0].Name);
    }

    [Fact]
    public void Parse_PostEntity_RequestCategoryIdReference() {
        var doc        = ParseVector1();
        var post       = doc.Entities.First(e => e.Name == "Post");
        var request    = post.Views.First(v => v.Name == "request");
        var categoryId = request.Fields.First(f => f.Name == "category_id");
        Assert.Contains(ViewOption.Omit, categoryId.Options);
        var reference = Assert.IsType<Reference>(categoryId.Assignment);
        Assert.Equal("category.id", reference.QualifiedName);
    }

    [Fact]
    public void Parse_PostEntity_ResponseView() {
        var doc      = ParseVector1();
        var post     = doc.Entities.First(e => e.Name == "Post");
        var response = post.Views.First(v => v.Name == "response");
        Assert.Equal(5, response.Fields.Length);
    }

    [Fact]
    public void Parse_PostEntity_ResponseNestedUser() {
        var doc      = ParseVector1();
        var post     = doc.Entities.First(e => e.Name == "Post");
        var response = post.Views.First(v => v.Name == "response");
        var user     = response.Fields.First(f => f.Name == "user");
        Assert.Equal("User.response", user.Type);
        Assert.Contains(ViewOption.OmitAll, user.Options);
        Assert.Single(user.Children);
        Assert.Equal("id", user.Children[0].Name);
        Assert.Single(user.Children[0].Notes);
    }
}
