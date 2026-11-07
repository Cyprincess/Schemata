using System;
using System.Text.Json.Nodes;
using Schemata.Scim.Models;
using Schemata.Scim.Services;
using Xunit;

namespace Schemata.Scim.Tests;

[Trait("Layer", "Unit")]
public sealed class ScimFilterShould
{
    private static readonly JsonObject Resource = new() {
        ["schemas"] = new JsonArray(ScimConstants.UserSchema),
        ["id"] = "user-1",
        ["userName"] = "alice",
        ["displayName"] = "Alice Example",
        ["active"] = true,
        ["name"] = new JsonObject { ["givenName"] = "Alice", ["familyName"] = "Example" },
        ["emails"] = new JsonArray(
            new JsonObject { ["value"] = "alice@work.example", ["type"] = "work", ["primary"] = true },
            new JsonObject { ["value"] = "alice@home.example", ["type"] = "home" }),
        ["meta"] = new JsonObject { ["lastModified"] = "2026-01-02T03:04:05Z" },
    };

    [Theory]
    [InlineData("userName Eq \"alice\"", true)]
    [InlineData("USERNAME eq \"alice\"", true)]
    [InlineData("urn:ietf:params:scim:schemas:core:2.0:User:userName eq \"alice\"", true)]
    [InlineData("userName eq \"ALICE\"", true)]
    [InlineData("id eq \"USER-1\"", false)]
    public void Attribute_Rules_Preserve_Schema_Semantics(string expression, bool expected) {
        var filter = ScimFilterParser.Parse(expression, ScimSchemaRegistry.CoreUser);

        Assert.Equal(expected, ScimFilterEvaluator.Matches(filter, Resource));
    }

    [Theory]
    [InlineData("userName sw \"ali\" and name.familyName co \"amp\"", true)]
    [InlineData("displayName ew \"EXAMPLE\" or userName eq \"bob\"", true)]
    [InlineData("not (userName eq \"bob\") and active eq true", true)]
    [InlineData("userName eq \"bob\" or userName eq \"alice\" and active eq false", false)]
    [InlineData("(userName eq \"bob\" or userName eq \"alice\") and active eq true", true)]
    [InlineData("meta.lastModified gt \"2026-01-01T00:00:00Z\"", true)]
    public void Logical_And_Comparison_Operators_Follow_Rfc_Precedence(string expression, bool expected) {
        var filter = ScimFilterParser.Parse(expression, ScimSchemaRegistry.CoreUser);

        Assert.Equal(expected, ScimFilterEvaluator.Matches(filter, Resource));
    }

    [Fact]
    public void Value_Path_Matches_Subattributes_In_The_Same_Array_Element() {
        var matching = ScimFilterParser.Parse("emails[type eq \"work\" and value ew \"@work.example\"]", ScimSchemaRegistry.CoreUser);
        var mismatch = ScimFilterParser.Parse("emails[type eq \"home\" and value ew \"@work.example\"]", ScimSchemaRegistry.CoreUser);

        Assert.True(ScimFilterEvaluator.Matches(matching, Resource));
        Assert.False(ScimFilterEvaluator.Matches(mismatch, Resource));
    }

    [Theory]
    [InlineData("")]
    [InlineData("userName")]
    [InlineData("userName regex \"alice\"")]
    [InlineData("userName eq")]
    [InlineData("userName eq\"alice\"")]
    [InlineData("(userName eq \"alice\"")]
    [InlineData("unknown eq \"value\"")]
    [InlineData("active gt false")]
    public void Malformed_Or_Unsupported_Filters_Return_Invalid_Filter(string expression) {
        var error = Assert.Throws<ScimProtocolException>(() => ScimFilterParser.Parse(expression, ScimSchemaRegistry.CoreUser));

        Assert.Equal(400, error.Status);
        Assert.Equal(ScimConstants.InvalidFilter, error.ScimType);
        Assert.DoesNotContain("alice@work.example", error.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Filter_Depth_Is_Bounded_Before_Evaluation() {
        var expression = "userName pr";
        for (var index = 0; index < 33; index++) {
            expression = "not (" + expression + ")";
        }

        var error = Assert.Throws<ScimProtocolException>(() => ScimFilterParser.Parse(expression, ScimSchemaRegistry.CoreUser));

        Assert.Equal(ScimConstants.InvalidFilter, error.ScimType);
    }

    [Fact]
    public void Projection_Returns_Requested_And_Always_Attributes_But_Never_Password_Or_Version() {
        var resource = Resource.DeepClone().AsObject();
        resource["password"] = "do-not-return";
        resource["meta"]!.AsObject()["version"] = "W/\"secret-version\"";

        var projected = ScimProjection.Apply(resource, [ScimSchemaRegistry.CoreUser], ["name.givenName"], null);

        Assert.Equal("user-1", projected["id"]?.GetValue<string>());
        Assert.Equal("Alice", projected["name"]?["givenName"]?.GetValue<string>());
        Assert.False(projected["name"]!.AsObject().ContainsKey("familyName"));
        Assert.False(projected.ContainsKey("userName"));
        Assert.False(projected.ContainsKey("password"));
        Assert.False(projected.ContainsKey("meta"));
    }

    [Fact]
    public void Excluded_Attributes_Cannot_Remove_Always_Attributes() {
        var resource = Resource.DeepClone().AsObject();
        resource["meta"]!.AsObject()["version"] = "W/\"omitted\"";
        var projected = ScimProjection.Apply(resource, [ScimSchemaRegistry.CoreUser], null, ["id", "displayName"]);

        Assert.Equal("user-1", projected["id"]?.GetValue<string>());
        Assert.False(projected.ContainsKey("displayName"));
        Assert.Equal("alice", projected["userName"]?.GetValue<string>());
        Assert.False(projected["meta"]!.AsObject().ContainsKey("version"));
    }

    [Fact]
    public void Extension_Uri_Keys_Are_Exact_While_Ordinary_Attribute_Names_Are_CaseInsensitive() {
        const string uri = "urn:example:params:scim:schemas:extension:2.0:Profile";
        var extension = new ScimSchema {
            Id = uri,
            Attributes = [new() { Name = "department", Type = ScimConstants.StringType }],
        };
        var resource = Resource.DeepClone().AsObject();
        resource["schemas"] = new JsonArray(ScimConstants.UserSchema, uri);
        resource[uri] = new JsonObject { ["Department"] = "Engineering" };
        var filter = ScimFilterParser.Parse(uri + ":department eq \"engineering\"", [ScimSchemaRegistry.CoreUser, extension]);

        Assert.True(ScimFilterEvaluator.Matches(filter, resource));

        resource.Remove(uri);
        resource[uri.ToUpperInvariant()] = new JsonObject { ["department"] = "Engineering" };
        Assert.False(ScimFilterEvaluator.Matches(filter, resource));
        Assert.Throws<ScimProtocolException>(() => ScimFilterParser.Parse(
            uri.ToUpperInvariant() + ":department eq \"engineering\"", [ScimSchemaRegistry.CoreUser, extension]));
    }
}
