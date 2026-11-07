using System;
using System.Linq;
using System.Text.Json.Nodes;
using Schemata.Scim.Models;
using Schemata.Scim.Services;
using Xunit;

namespace Schemata.Scim.Tests;

[Trait("Layer", "Unit")]
public sealed class ScimPatchShould
{
    [Fact]
    public void Operations_Apply_In_Order_To_A_Clone() {
        var original = Group("user-1", "user-2");
        var patch = Patch(
            Operation("remove", "members[value eq \"user-1\"]"),
            Operation("add", "members", new JsonArray(new JsonObject { ["value"] = "user-3" })),
            Operation("replace", "displayName", "New name"));

        var updated = ScimPatchProcessor.Apply(original, patch, ScimSchemaRegistry.CoreGroup);

        Assert.Equal(new[] { "user-2", "user-3" }, Members(updated));
        Assert.Equal(new[] { "user-1", "user-2" }, Members(original));
        Assert.Equal("New name", updated["displayName"]?.GetValue<string>());
        Assert.Equal("Original", original["displayName"]?.GetValue<string>());
    }

    [Fact]
    public void Replace_Updates_Only_The_Selected_Value_And_Subattribute() {
        var original = new JsonObject {
            ["schemas"] = new JsonArray(ScimConstants.UserSchema),
            ["emails"] = new JsonArray(
                new JsonObject { ["value"] = "work@example.test", ["type"] = "work", ["display"] = "Work" },
                new JsonObject { ["value"] = "home@example.test", ["type"] = "home", ["display"] = "Home" }),
        };
        var patch = Patch(
            Operation("replace", "emails[type eq \"home\"].display", "Replacement"),
            Operation("replace", "emails[type eq \"work\"]", new JsonObject { ["display"] = "Third" }));

        var updated = ScimPatchProcessor.Apply(original, patch, ScimSchemaRegistry.CoreUser);

        Assert.Equal("work@example.test", updated["emails"]![0]!["value"]?.GetValue<string>());
        Assert.Equal("Third", updated["emails"]![0]!["display"]?.GetValue<string>());
        Assert.Equal("Replacement", updated["emails"]![1]!["display"]?.GetValue<string>());
        Assert.Equal("Home", original["emails"]![1]!["display"]?.GetValue<string>());
    }

    [Fact]
    public void Add_Is_Idempotent_For_An_Existing_Array_Value() {
        var original = Group("user-1");
        var duplicate = new JsonObject { ["value"] = "user-1", ["display"] = "User 1", ["type"] = "User" };

        var updated = ScimPatchProcessor.Apply(original, Patch(Operation("add", "members", duplicate)), ScimSchemaRegistry.CoreGroup);

        Assert.Single(updated["members"]!.AsArray());
    }

    [Fact]
    public void Replace_Without_A_Matching_Value_Returns_No_Target() {
        var error = Assert.Throws<ScimProtocolException>(() => ScimPatchProcessor.Apply(
            Group("user-1"),
            Patch(Operation("replace", "members[value eq \"missing\"].display", "Nobody")),
            ScimSchemaRegistry.CoreGroup));

        Assert.Equal(ScimConstants.NoTarget, error.ScimType);
    }

    [Fact]
    public void Remove_Without_A_Path_Returns_NoTarget_Before_Touching_The_Resource() {
        var original = Group("user-1");

        var error = Assert.Throws<ScimProtocolException>(() => ScimPatchProcessor.Apply(
            original, Patch(Operation("remove", null)), ScimSchemaRegistry.CoreGroup));

        Assert.Equal(ScimConstants.NoTarget, error.ScimType);
        Assert.Equal(new[] { "user-1" }, Members(original));
    }

    [Fact]
    public void ReadOnly_And_Required_Removals_Return_Mutability() {
        var original = Group("user-1");
        original["id"] = "group-1";

        var readOnly = Assert.Throws<ScimProtocolException>(() => ScimPatchProcessor.Apply(
            original, Patch(Operation("replace", "id", "other")), ScimSchemaRegistry.CoreGroup));
        var required = Assert.Throws<ScimProtocolException>(() => ScimPatchProcessor.Apply(
            original, Patch(Operation("remove", "displayName")), ScimSchemaRegistry.CoreGroup));

        Assert.Equal(ScimConstants.Mutability, readOnly.ScimType);
        Assert.Equal(ScimConstants.Mutability, required.ScimType);
    }

    [Fact]
    public void Operation_Limit_Is_Enforced_Before_Execution() {
        var operations = Enumerable.Range(0, 101).Select(index => Operation("replace", "displayName", $"Name {index}")).ToArray();
        var original = Group("user-1");

        var error = Assert.Throws<ScimProtocolException>(() => ScimPatchProcessor.Apply(
            original, new ScimPatchDocument { Operations = operations }, ScimSchemaRegistry.CoreGroup));

        Assert.Equal(ScimConstants.InvalidValue, error.ScimType);
        Assert.Equal("Original", original["displayName"]?.GetValue<string>());
    }

    [Fact]
    public void Password_Input_Is_Redacted_From_Protocol_Errors() {
        var error = Assert.Throws<ScimProtocolException>(() => ScimPatchProcessor.Apply(
            Group("user-1"),
            Patch(Operation("add", null, new JsonObject { ["password"] = "a-secret-value" })),
            ScimSchemaRegistry.CoreGroup));

        Assert.Equal(ScimConstants.InvalidValue, error.ScimType);
        Assert.DoesNotContain("a-secret-value", error.Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("add", false)]
    [InlineData("replace", false)]
    [InlineData("replace", true)]
    public void Nonboolean_Primary_Returns_InvalidValue_And_Preserves_Input(string verb, bool selected) {
        var original = new JsonObject {
            ["schemas"] = new JsonArray(ScimConstants.UserSchema),
            ["emails"] = new JsonArray(new JsonObject { ["value"] = "user@example.test", ["type"] = "work", ["primary"] = false }),
        };
        var before = original.ToJsonString();
        var operation = selected
            ? Operation(verb, "emails[type eq \"work\"].PRIMARY", "private-value")
            : Operation(verb, null, new JsonObject { ["emails"] = new JsonArray(new JsonObject { ["value"] = "other@example.test", ["PRIMARY"] = "private-value" }) });

        var error = Assert.Throws<ScimProtocolException>(() => ScimPatchProcessor.Apply(original, Patch(operation), ScimSchemaRegistry.CoreUser));

        Assert.Equal(400, error.Status);
        Assert.Equal(ScimConstants.InvalidValue, error.ScimType);
        Assert.DoesNotContain("private-value", error.Detail, StringComparison.Ordinal);
        Assert.Equal(before, original.ToJsonString());
    }

    private static JsonObject Group(params string[] members) {
        return new JsonObject {
            ["schemas"] = new JsonArray(ScimConstants.GroupSchema),
            ["displayName"] = "Original",
            ["members"] = new JsonArray(members.Select((member, index) => (JsonNode)new JsonObject {
                ["value"] = member,
                ["display"] = $"User {index + 1}",
                ["type"] = "User",
            }).ToArray()),
        };
    }

    private static string[] Members(JsonObject resource) {
        return resource["members"]!.AsArray().Select(member => member!["value"]!.GetValue<string>()).ToArray();
    }

    private static ScimPatchDocument Patch(params ScimPatchOperation[] operations) {
        return new() { Operations = operations };
    }

    private static ScimPatchOperation Operation(string op, string? path = null, JsonNode? value = null) {
        return new() { Op = op, Path = path, Value = value };
    }
}
