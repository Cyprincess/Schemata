using System.Collections.Generic;
using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Schemata.Abstractions.Entities;
using Schemata.Insight.Skeleton.Models;

namespace Schemata.Report.Foundation.Queries;

/// <summary>One page of rows from a persisted report snapshot.</summary>
[JsonConverter(typeof(ReadSnapshotResponseConverter))]
public sealed class ReadSnapshotResponse : ICanonicalName
{
    /// <summary>Rows decoded from the snapshot chunks needed for this page.</summary>
    public IList<IReadOnlyDictionary<string, object?>> Rows { get; set; } = [];

    public IReadOnlyList<FieldDescriptor> Schema { get; set; } = [];

    /// <summary>Opaque token for the next page, or <see langword="null" /> after the final row.</summary>
    public string? NextPageToken { get; set; }

    string? ICanonicalName.Name { get; set; }

    string? ICanonicalName.CanonicalName { get; set; }
}

public sealed class ReadSnapshotResponseConverter : JsonConverter<ReadSnapshotResponse>
{
    public override ReadSnapshotResponse Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        var schema = root.GetProperty("schema").Deserialize<FieldDescriptor[]>(options) ?? [];
        var response = new ReadSnapshotResponse { Schema = schema };
        foreach (var row in root.GetProperty("rows").EnumerateArray()) {
            var values = new Dictionary<string, object?>();
            foreach (var field in row.EnumerateObject()) {
                var descriptor = schema.FirstOrDefault(item => item.Name == field.Name);
                values.Add(field.Name, descriptor is { Type: FieldType.Dynamic, IsList: false }
                    ? InsightValueModel.Decode(field.Value, descriptor) : field.Value.Clone());
            }
            response.Rows.Add(values);
        }
        if (root.TryGetProperty("next_page_token", out var token)) response.NextPageToken = token.GetString();
        return response;
    }

    public override void Write(Utf8JsonWriter writer, ReadSnapshotResponse value, JsonSerializerOptions options) {
        writer.WriteStartObject();
        writer.WritePropertyName("rows");
        writer.WriteStartArray();
        foreach (var row in value.Rows) JsonSerializer.Serialize(writer, InsightValueModel.EncodeRow(row, value.Schema), options);
        writer.WriteEndArray();
        writer.WritePropertyName("schema");
        JsonSerializer.Serialize(writer, value.Schema, options);
        if (value.NextPageToken is not null) writer.WriteString("next_page_token", value.NextPageToken);
        writer.WriteEndObject();
    }
}
