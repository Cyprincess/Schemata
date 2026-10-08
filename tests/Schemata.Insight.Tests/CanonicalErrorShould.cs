using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json.Serialization;
using System.Linq;
using Schemata.Abstractions;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;
using Schemata.Insight.Foundation.Materialization;
using Schemata.Insight.Foundation.Planning;
using Schemata.Insight.Skeleton.Models;
using Xunit;

namespace Schemata.Insight.Tests;

[Trait("Category", "Unit")]
public sealed class CanonicalErrorShould
{
    [Trait("Layer", "Unit")]
    [Theory]
    [InlineData("UNKNOWN_SOURCE_NAME", 404, "NOT_FOUND")]
    [InlineData("UNIMPLEMENTED", 501, "UNIMPLEMENTED")]
    [InlineData("INVALID_ARGUMENT", 400, "INVALID_ARGUMENT")]
    public void Rejection_ProducesCanonicalEnvelope_WithStableReasonAndMetadata(string reason, int code, string status) {
        var args = new Dictionary<string, string?> { ["name"] = "missing", ["optional"] = null };
        var rejection = new InsightValidationException(reason, SchemataResources.INSIGHT_UNKNOWN_SOURCE, args);
        args["name"] = "changed";
        var response = Assert.IsType<ErrorResponse>(rejection.CreateErrorResponse(locale: "fr"));
        Assert.Equal(code, response.Error!.Code);
        Assert.Equal(status, response.Error.Status);
        Assert.Equal("Unknown resource 'missing'.", response.Error.Message);
        var details = response.Error.Details;
        Assert.NotNull(details);
        var info = Assert.Single(details.OfType<ErrorInfoDetail>());
        Assert.Equal(reason, info.Reason);
        Assert.Equal("schemata.insight", info.Domain);
        Assert.Equal("missing", info.Metadata!["name"]);
        Assert.Equal("", info.Metadata["optional"]);
        Assert.Equal("Ressource 'missing' inconnue.", Assert.Single(details.OfType<LocalizedMessageDetail>()).Message);
    }

    [Trait("Layer", "Unit")]
    [Fact]
    public void DriverRow_RejectsUnknownObject_BeforeSerialization() {
        var row = new Dictionary<string, object?> { ["unknown"] = new object() };
        var error = Assert.Throws<InvalidArgumentException>(() => RowMaterializer.NormalizeRow(row));
        var response = Assert.IsType<ErrorResponse>(error.CreateErrorResponse());
        Assert.Equal("INVALID_ARGUMENT", response.Error!.Status);
        var info = Assert.Single(response.Error.Details!.OfType<ErrorInfoDetail>());
        Assert.Equal(SchemataResources.INSIGHT_VALUE_TYPE_UNSUPPORTED, info.Reason);
        Assert.Equal("System.Object", info.Metadata!["type"]);
    }

    [Trait("Layer", "Unit")]
    [Fact]
    public void PublicModel_RejectsUndefinedEnum_InsteadOfNumericName() {
        var error = Assert.Throws<InvalidArgumentException>(() => RowMaterializer.ToRow(new EnumRow { State = (State)7 }, [], "s"));
        var response = Assert.IsType<ErrorResponse>(error.CreateErrorResponse());
        Assert.Equal(SchemataResources.INSIGHT_VALUE_TYPE_UNSUPPORTED, Assert.Single(response.Error!.Details!.OfType<ErrorInfoDetail>()).Reason);
    }

    [Trait("Layer", "Unit")]
    [Fact]
    public void DriverRow_ProjectsDeclaredClrChildFields_WithoutExposingExtraMembers() {
        var children = new[] { new DeclaredChild { Name = "visible", Extra = "private", Secret = "ignored" } };
        var input = new Dictionary<string, object?> { ["children"] = children };
        var output = RowMaterializer.NormalizeRow(input, [new("children", FieldType.Object, null, true,
            [new("name", FieldType.String, null, false, [])])]);
        var projected = Assert.IsAssignableFrom<IReadOnlyList<object?>>(output["children"]);
        var child = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(Assert.Single(projected));
        Assert.Equal("visible", child["name"]);
        Assert.False(child.ContainsKey("extra"));
        Assert.False(child.ContainsKey("secret"));
    }

    [Trait("Layer", "Unit")]
    [Fact]
    public void DriverRow_RejectsIgnoredGetter_EvenWhenDriverSchemaDeclaresIt() {
        var input = new Dictionary<string, object?> { ["child"] = new DeclaredChild { Secret = "ignored" } };
        var failure = Assert.Throws<InsightValidationException>(() => RowMaterializer.NormalizeRow(input,
            [new("child", FieldType.Object, null, false, [new("secret", FieldType.String, null, false, [])])]));
        Assert.Equal("INVALID_ARGUMENT", failure.Reason);
        Assert.Equal("secret", failure.Metadata!["field"]);
    }

    [Trait("Layer", "Unit")]
    [Fact]
    public void DriverRow_RejectsClrChild_WhenObjectSchemaDoesNotDeclareMembers() {
        var input = new Dictionary<string, object?> { ["child"] = new DeclaredChild { Name = "unregistered" } };
        var failure = Assert.Throws<InvalidArgumentException>(() => RowMaterializer.NormalizeRow(input,
            [new("child", FieldType.Object, null, false, [])]));
        var response = Assert.IsType<ErrorResponse>(failure.CreateErrorResponse());
        Assert.Equal(SchemataResources.INSIGHT_VALUE_TYPE_UNSUPPORTED, Assert.Single(response.Error!.Details!.OfType<ErrorInfoDetail>()).Reason);
    }

    [Trait("Layer", "Unit")]
    [Fact]
    public void DriverRow_PreservesTypedDecimalMaps_InsideDeclaredClrChildren() {
        const decimal exact = 7922816251426433759354395033.5m;
        var input = new Dictionary<string, object?> { ["child"] = new MapChild {
            Amounts = new() { ["exact"] = exact, ["missing"] = null },
            ExactAmounts = new() { ["exact"] = exact },
        } };
        var row = RowMaterializer.NormalizeRow(input, [new("child", FieldType.Object, null, false,
            [new("amounts", FieldType.Map, null, false, [new("*", FieldType.Decimal, null, false, [])]),
             new("exact_amounts", FieldType.Map, null, false, [new("*", FieldType.Decimal, null, false, [])])])]);
        var child = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(row["child"]);
        var amounts = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(child["amounts"]);
        Assert.Equal(exact, Assert.IsType<decimal>(amounts["exact"]));
        Assert.Null(amounts["missing"]);
        var exactAmounts = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(child["exact_amounts"]);
        Assert.Equal(exact, Assert.IsType<decimal>(exactAmounts["exact"]));
    }

    [Trait("Layer", "Unit")]
    [Fact]
    public void DriverRow_ProjectsReadOnlyMapValues_ThroughDeclaredChildFields() {
        var input = new Dictionary<string, object?> { ["child"] = new MapChild {
            Members = new System.Collections.ObjectModel.ReadOnlyDictionary<string, DeclaredChild>(
                new Dictionary<string, DeclaredChild> { ["one"] = new() { Name = "visible", Secret = "ignored", Extra = "private" } }),
        } };
        var row = RowMaterializer.NormalizeRow(input, [new("child", FieldType.Object, null, false,
            [new("members", FieldType.Map, null, false,
                [new("*", FieldType.Object, null, false, [new("name", FieldType.String, null, false, [])])])])]);
        var child = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(row["child"]);
        var members = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(child["members"]);
        var member = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(members["one"]);
        Assert.Equal("visible", member["name"]);
        Assert.False(member.ContainsKey("secret"));
        Assert.False(member.ContainsKey("extra"));
    }

    [Trait("Layer", "Unit")]
    [Fact]
    public void DriverRow_RejectsIgnoredMember_InDeclaredTypedMapValue() {
        var input = new Dictionary<string, object?> { ["child"] = new MapChild {
            Members = new Dictionary<string, DeclaredChild> { ["one"] = new() { Secret = "ignored" } },
        } };
        var failure = Assert.Throws<InsightValidationException>(() => RowMaterializer.NormalizeRow(input,
            [new("child", FieldType.Object, null, false, [new("members", FieldType.Map, null, false,
                [new("*", FieldType.Object, null, false, [new("secret", FieldType.String, null, false, [])])])])]));
        Assert.Equal("INVALID_ARGUMENT", failure.Reason);
        Assert.Equal("secret", failure.Metadata!["field"]);
    }

    [Trait("Layer", "Unit")]
    [Fact]
    public void DriverRow_PreservesDeclaredNullableStructs_InMembersMapsAndLists() {
        var value = new NullableParent {
            Child = new NullableChild { Name = "member", Secret = "ignored" },
            Members = new() { ["present"] = new NullableChild { Name = "map", Secret = "ignored" }, ["missing"] = null },
            Children = [new NullableChild { Name = "list", Secret = "ignored" }, null],
        };
        var schema = SchemaBuilder.For(typeof(NullableParent), [], "p");
        var row = RowMaterializer.NormalizeRow(new Dictionary<string, object?> { ["parent"] = value },
            [new("parent", FieldType.Object, null, false, [..schema])]);
        var parent = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(row["parent"]);
        var child = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(parent["child"]);
        Assert.Equal("member", child["name"]);
        Assert.False(child.ContainsKey("secret"));
        Assert.Null(parent["missing"]);
        var map = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(parent["members"]);
        var mapChild = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(map["present"]);
        Assert.Equal("map", mapChild["name"]);
        Assert.False(mapChild.ContainsKey("secret"));
        Assert.Null(map["missing"]);
        var list = Assert.IsAssignableFrom<IReadOnlyList<object?>>(parent["children"]);
        var listChild = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(list[0]);
        Assert.Equal("list", listChild["name"]);
        Assert.False(listChild.ContainsKey("secret"));
        Assert.Null(list[1]);
    }

    public sealed class NullableParent {
        public NullableChild? Child { get; init; }
        public NullableChild? Missing { get; init; }
        public Dictionary<string, NullableChild?> Members { get; init; } = [];
        public List<NullableChild?> Children { get; init; } = [];
    }

    public struct NullableChild {
        public string? Name { get; init; }
        [JsonIgnore] public string? Secret { get; init; }
    }

    public sealed class MapChild {
        public Dictionary<string, decimal?> Amounts { get; init; } = [];
        public Dictionary<string, decimal> ExactAmounts { get; init; } = [];
        public IReadOnlyDictionary<string, DeclaredChild> Members { get; init; } = new Dictionary<string, DeclaredChild>();
    }

    public sealed class DeclaredChild {
        public string? Name { get; init; }
        public string? Extra { get; init; }
        [JsonIgnore] public string? Secret { get; init; }
    }

    public enum State { Ready }
    public sealed class EnumRow { public State State { get; init; } }
}
