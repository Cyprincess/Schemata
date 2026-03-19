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
        return doc!;
    }

    // ── Namespace ─────────────────────────────────────────────────────

    [Fact]
    public void ParseNamespace() {
        var doc = ParseVector1();
        Assert.Equal("DSL.Tests.Vectors", doc.Namespace);
    }

    // ── Traits ────────────────────────────────────────────────────────

    [Fact]
    public void ParseAllTraits() {
        var doc = ParseVector1();
        Assert.Equal(3, doc.Traits.Length);
        Assert.Contains(doc.Traits, t => t.Name == "Identifier");
        Assert.Contains(doc.Traits, t => t.Name == "Timestamp");
        Assert.Contains(doc.Traits, t => t.Name == "Entity");
    }

    [Fact]
    public void ParseIdentifierTrait() {
        var doc   = ParseVector1();
        var trait = doc.Traits.First(t => t.Name == "Identifier");
        Assert.Single(trait.Notes);
        Assert.Single(trait.Fields);
        Assert.Equal("long", trait.Fields[0].Type);
        Assert.Equal("id", trait.Fields[0].Name);
        Assert.Contains(FieldOption.PrimaryKey, trait.Fields[0].Options);
    }

    [Fact]
    public void ParseTimestampTrait() {
        var doc   = ParseVector1();
        var trait = doc.Traits.First(t => t.Name == "Timestamp");
        Assert.Single(trait.Notes);
        Assert.Equal(2, trait.Fields.Length);
        Assert.True(trait.Fields[0].Nullable); // timestamp? creation_date
        Assert.Equal("timestamp", trait.Fields[0].Type);
        Assert.Equal("creation_date", trait.Fields[0].Name);
        Assert.True(trait.Fields[1].Nullable); // timestamp? modification_date
        Assert.Equal("modification_date", trait.Fields[1].Name);
    }

    [Fact]
    public void ParseTimestampCreationDateNotes() {
        var doc   = ParseVector1();
        var trait = doc.Traits.First(t => t.Name == "Timestamp");
        // creation_date has two notes in its body
        Assert.Equal(2, trait.Fields[0].Notes.Length);
    }

    [Fact]
    public void ParseEntityTrait() {
        var doc   = ParseVector1();
        var trait = doc.Traits.First(t => t.Name == "Entity");
        Assert.Single(trait.Uses);
        // Use Identifier, Timestamp — two qualified names
        Assert.Equal(2, trait.Uses[0].QualifiedNames.Length);
        Assert.Contains("Identifier", trait.Uses[0].QualifiedNames);
        Assert.Contains("Timestamp", trait.Uses[0].QualifiedNames);
    }

    // ── Entities ──────────────────────────────────────────────────────

    [Fact]
    public void ParseAllEntities() {
        var doc = ParseVector1();
        Assert.Equal(3, doc.Entities.Length);
        Assert.Contains(doc.Entities, e => e.Name == "User");
        Assert.Contains(doc.Entities, e => e.Name == "Category");
        Assert.Contains(doc.Entities, e => e.Name == "Post");
    }

    // ── User entity ──────────────────────────────────────────────────

    [Fact]
    public void ParseUserEntity() {
        var doc  = ParseVector1();
        var user = doc.Entities.First(e => e.Name == "User");
        Assert.Single(user.Notes);
        Assert.Single(user.Uses);
        Assert.Equal("Entity", user.Uses[0].QualifiedNames[0]);
        Assert.Equal(4, user.Fields.Length); // email_address, phone_number, password, nickname
        Assert.Single(user.Views);           // response
    }

    [Fact]
    public void ParseUserFields() {
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
    public void ParseUserResponseView() {
        var doc      = ParseVector1();
        var user     = doc.Entities.First(e => e.Name == "User");
        var response = user.Views[0];
        Assert.Equal("response", response.Name);
        // The body-context parser consumes "nickname email_address [omit] { ... }"
        // as a typed view field (type=nickname, name=email_address) because [omit]
        // acts as a continuation token. This yields 5 fields total.
        Assert.Equal(5, response.Fields.Length);
    }

    [Fact]
    public void ParseUserResponseViewFieldNames() {
        var doc      = ParseVector1();
        var user     = doc.Entities.First(e => e.Name == "User");
        var response = user.Views[0];
        Assert.Equal("id", response.Fields[0].Name);
        // "nickname email_address [omit]" parses as typed field (type=nickname, name=email_address)
        Assert.Equal("email_address", response.Fields[1].Name);
        Assert.Equal("nickname", response.Fields[1].Type);
        Assert.Equal("obfuscated_email_address", response.Fields[2].Name);
        Assert.Equal("phone_number", response.Fields[3].Name);
        Assert.Equal("obfuscated_phone_number", response.Fields[4].Name);
    }

    [Fact]
    public void ParseFunctionCallInView() {
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
    public void ParseObfuscatedPhoneNumberAssignment() {
        var doc        = ParseVector1();
        var user       = doc.Entities.First(e => e.Name == "User");
        var response   = user.Views[0];
        var obfuscated = response.Fields.First(f => f.Name == "obfuscated_phone_number");
        var fn         = Assert.IsType<FunctionCall>(obfuscated.Assignment);
        Assert.Equal("obfuscate", fn.Name);
    }

    [Fact]
    public void ParseOmitOptionsInUserResponse() {
        var doc      = ParseVector1();
        var user     = doc.Entities.First(e => e.Name == "User");
        var response = user.Views[0];

        // [0] id — no options
        Assert.Equal(0, response.Fields[0].Options.Length);

        // [1] nickname email_address [omit] — has [omit]
        Assert.Contains(ViewOption.Omit, response.Fields[1].Options);

        // [2] obfuscated_email_address [omit]
        Assert.Contains(ViewOption.Omit, response.Fields[2].Options);

        // [3] phone_number [omit]
        Assert.Contains(ViewOption.Omit, response.Fields[3].Options);

        // [4] obfuscated_phone_number [omit]
        Assert.Contains(ViewOption.Omit, response.Fields[4].Options);
    }

    // ── Category entity ──────────────────────────────────────────────

    [Fact]
    public void ParseCategoryEntity() {
        var doc      = ParseVector1();
        var category = doc.Entities.First(e => e.Name == "Category");
        Assert.Single(category.Notes);
        Assert.Single(category.Uses);
        Assert.Single(category.Fields);         // Name
        Assert.Equal(2, category.Views.Length); // request, response
    }

    [Fact]
    public void ParseCategoryNameField() {
        var doc      = ParseVector1();
        var category = doc.Entities.First(e => e.Name == "Category");
        var field    = category.Fields[0];
        Assert.Equal("string", field.Type);
        Assert.Equal("Name", field.Name);
        Assert.Contains(FieldOption.Required, field.Options); // [not null] -> Required
    }

    [Fact]
    public void ParseCategoryRequestView() {
        var doc     = ParseVector1();
        var cat     = doc.Entities.First(e => e.Name == "Category");
        var request = cat.Views.First(v => v.Name == "request");
        Assert.Single(request.Fields);
        Assert.Equal("name", request.Fields[0].Name);
    }

    [Fact]
    public void ParseCategoryResponseView() {
        var doc      = ParseVector1();
        var cat      = doc.Entities.First(e => e.Name == "Category");
        var response = cat.Views.First(v => v.Name == "response");
        Assert.Equal(3, response.Fields.Length); // id, name, timestamp expiration_date
        Assert.Equal("id", response.Fields[0].Name);
        Assert.Equal("name", response.Fields[1].Name);
        Assert.Equal("expiration_date", response.Fields[2].Name);
    }

    [Fact]
    public void ParseNowFunctionInView() {
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

    // ── Post entity ──────────────────────────────────────────────────

    [Fact]
    public void ParsePostEntity() {
        var doc  = ParseVector1();
        var post = doc.Entities.First(e => e.Name == "Post");
        Assert.Single(post.Uses);
        Assert.Single(post.Enumerations);    // Status enum
        Assert.Single(post.Pointers);        // Index category_id
        Assert.Equal(2, post.Views.Length);  // request, response
        Assert.Equal(5, post.Fields.Length); // category_id, user_id, status, title, body
    }

    [Fact]
    public void ParsePostStatusEnum() {
        var doc    = ParseVector1();
        var post   = doc.Entities.First(e => e.Name == "Post");
        var status = post.Enumerations[0];
        Assert.Equal("Status", status.Name);
        Assert.Equal(2, status.Values.Length);
        Assert.Equal("Draft", status.Values[0].Name);
        Assert.Equal("Published", status.Values[1].Name);
        Assert.Single(status.Values[0].Notes);          // Draft has a note
        Assert.Equal(0, status.Values[1].Notes.Length); // Published has no notes
    }

    [Fact]
    public void ParsePostFields() {
        var doc  = ParseVector1();
        var post = doc.Entities.First(e => e.Name == "Post");

        Assert.Equal("category_id", post.Fields[0].Name);
        Assert.Equal("long", post.Fields[0].Type);

        Assert.Equal("user_id", post.Fields[1].Name);
        Assert.Equal("long", post.Fields[1].Type);
        Assert.Contains(FieldOption.BTree, post.Fields[1].Options);

        Assert.Equal("status", post.Fields[2].Name);
        Assert.Equal("Status", post.Fields[2].Type);

        Assert.Equal("title", post.Fields[3].Name);
        Assert.Equal("string", post.Fields[3].Type);

        Assert.Equal("body", post.Fields[4].Name);
        Assert.Equal("text", post.Fields[4].Type);
    }

    [Fact]
    public void ParsePostStatusFieldDefault() {
        var doc  = ParseVector1();
        var post = doc.Entities.First(e => e.Name == "Post");
        // status field has {default 'Published'}
        var status = post.Fields.First(f => f.Name == "status");
        Assert.Single(status.Properties);
        Assert.Equal("default", status.Properties[0].Key);
        var lit = Assert.IsType<StringLiteral>(status.Properties[0].Value);
        Assert.Equal("Published", lit.Value);
    }

    [Fact]
    public void ParsePostTitleNote() {
        var doc   = ParseVector1();
        var post  = doc.Entities.First(e => e.Name == "Post");
        var title = post.Fields.First(f => f.Name == "title");
        Assert.Single(title.Notes);
        Assert.Equal("Title of the post", title.Notes[0].Text);
    }

    [Fact]
    public void ParsePostIndex() {
        var doc  = ParseVector1();
        var post = doc.Entities.First(e => e.Name == "Post");
        var idx  = post.Pointers[0];
        Assert.Single(idx.Columns);
        Assert.Equal("category_id", idx.Columns[0]);
        Assert.Contains(PointerOption.BTree, idx.Options);
    }

    [Fact]
    public void ParsePostRequestView() {
        var doc     = ParseVector1();
        var post    = doc.Entities.First(e => e.Name == "Post");
        var request = post.Views.First(v => v.Name == "request");
        // Fields: category [omit all], category_id [omit], status, title, body
        Assert.Equal(5, request.Fields.Length);
        Assert.Equal("category", request.Fields[0].Name);
        Assert.Equal("Category.response", request.Fields[0].Type);
        Assert.Contains(ViewOption.OmitAll, request.Fields[0].Options);
    }

    [Fact]
    public void ParsePostRequestCategoryChildren() {
        var doc      = ParseVector1();
        var post     = doc.Entities.First(e => e.Name == "Post");
        var request  = post.Views.First(v => v.Name == "request");
        var category = request.Fields.First(f => f.Name == "category");
        // category has a Note and a child field (id)
        Assert.Single(category.Notes);
        Assert.Single(category.Children);
        Assert.Equal("id", category.Children[0].Name);
    }

    [Fact]
    public void ParsePostRequestCategoryIdAssignment() {
        var doc        = ParseVector1();
        var post       = doc.Entities.First(e => e.Name == "Post");
        var request    = post.Views.First(v => v.Name == "request");
        var categoryId = request.Fields.First(f => f.Name == "category_id");
        Assert.Contains(ViewOption.Omit, categoryId.Options);
        var reference = Assert.IsType<Reference>(categoryId.Assignment);
        Assert.Equal("category.id", reference.QualifiedName);
    }

    [Fact]
    public void ParsePostResponseView() {
        var doc      = ParseVector1();
        var post     = doc.Entities.First(e => e.Name == "Post");
        var response = post.Views.First(v => v.Name == "response");
        // Fields: category [omit all], user [omit all], status, title, body
        Assert.Equal(5, response.Fields.Length);
    }

    [Fact]
    public void ParsePostResponseNestedUserField() {
        var doc      = ParseVector1();
        var post     = doc.Entities.First(e => e.Name == "Post");
        var response = post.Views.First(v => v.Name == "response");
        var user     = response.Fields.First(f => f.Name == "user");
        Assert.Equal("User.response", user.Type);
        Assert.Contains(ViewOption.OmitAll, user.Options);
        // user has a child: id with a Note
        Assert.Single(user.Children);
        Assert.Equal("id", user.Children[0].Name);
        Assert.Single(user.Children[0].Notes);
    }

    // ── Document-level counts ─────────────────────────────────────────

    [Fact]
    public void ParseDocumentHasNoTopLevelEnumerations() {
        var doc = ParseVector1();
        Assert.Equal(0, doc.Enumerations.Length);
    }
}
