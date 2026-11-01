using System;
using System.Collections.Generic;
using System.Text.Json;
using Schemata.Common;
using Schemata.Insight.Skeleton.Models;
using Schemata.Report.Foundation.Queries;
using Schemata.Report.Grpc;
using Xunit;

namespace Schemata.Report.Tests;

public class ReadSnapshotGrpcResponseShould
{
    private static readonly DateTime Stamp = new(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc);

    private static JsonElement Json(string text) {
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    private static ReadSnapshotResponse Snapshot(FieldDescriptor[] schema, IReadOnlyDictionary<string, object?> row) => new() {
        Schema = schema,
        Rows = [row],
    };

    private static IReadOnlyDictionary<string, object?> Row(object? value)
        => new Dictionary<string, object?> { ["created"] = value };

    [Fact]
    public void FromResponse_For_A_Wildcard_Dynamic_Field_Carries_The_Typed_Label() {
        var response = Snapshot(
            [new("*", FieldType.Dynamic, null, false, [])],
            Row(Json($$"""{"kind":"timestamp","value":"{{Stamp:O}}"}""")));

        var value = ReadSnapshotGrpcResponse.FromResponse(response).Rows[0].Fields["created"];

        Assert.Equal(Stamp.ToString("O"), value.StringValue);
        Assert.Equal(ScalarKind.Timestamp, value.TypeLabel);
    }

    [Fact]
    public void FromResponse_For_A_Wildcard_Dynamic_Field_Matches_An_Explicit_Field() {
        var wildcard = ReadSnapshotGrpcResponse.FromResponse(Snapshot(
            [new("*", FieldType.Dynamic, null, false, [])],
            Row(Json($$"""{"kind":"timestamp","value":"{{Stamp:O}}"}"""))));
        var explicitField = ReadSnapshotGrpcResponse.FromResponse(Snapshot(
            [new("created", FieldType.Dynamic, null, false, [])],
            Row(Json($$"""{"kind":"timestamp","value":"{{Stamp:O}}"}"""))));

        var expected = explicitField.Rows[0].Fields["created"];
        var actual   = wildcard.Rows[0].Fields["created"];

        Assert.Equal(expected.StringValue, actual.StringValue);
        Assert.Equal(expected.TypeLabel, actual.TypeLabel);
    }

    [Fact]
    public void ToResponse_For_A_Wildcard_Dynamic_Field_Decodes_The_Typed_Scalar() {
        var response = Snapshot(
            [new("*", FieldType.Dynamic, null, false, [])],
            Row(Json($$"""{"kind":"timestamp","value":"{{Stamp:O}}"}""")));

        var roundTripped = ReadSnapshotGrpcResponse.ToResponse(ReadSnapshotGrpcResponse.FromResponse(response));

        Assert.Equal(Stamp, Assert.IsType<DateTime>(roundTripped.Rows[0]["created"]));
    }

    [Fact]
    public void ToResponse_For_A_Wildcard_Typed_Field_Decodes_The_Typed_Scalar() {
        var response = Snapshot(
            [new("*", FieldType.Timestamp, null, false, [])],
            Row(Json($"\"{Stamp:O}\"")));

        var roundTripped = ReadSnapshotGrpcResponse.ToResponse(ReadSnapshotGrpcResponse.FromResponse(response));

        Assert.Equal(Stamp, Assert.IsType<DateTime>(roundTripped.Rows[0]["created"]));
    }

    [Fact]
    public void ToResponse_For_A_Wildcard_Top_Level_Field_Matches_Nested_Wildcard_Semantics() {
        var top = ReadSnapshotGrpcResponse.ToResponse(ReadSnapshotGrpcResponse.FromResponse(Snapshot(
            [new("*", FieldType.Timestamp, null, false, [])],
            Row(Json($"\"{Stamp:O}\"")))));
        var nested = ReadSnapshotGrpcResponse.ToResponse(ReadSnapshotGrpcResponse.FromResponse(Snapshot(
            [new("wrapper", FieldType.Object, null, false, [new("*", FieldType.Timestamp, null, false, [])])],
            new Dictionary<string, object?> { ["wrapper"] = Json($"{{\"created\":\"{Stamp:O}\"}}") })));
        var wrapper = Assert.IsType<Dictionary<string, object?>>(nested.Rows[0]["wrapper"]);

        Assert.Equal(Stamp, Assert.IsType<DateTime>(top.Rows[0]["created"]));
        Assert.Equal(Stamp, Assert.IsType<DateTime>(wrapper["created"]));
    }
}
