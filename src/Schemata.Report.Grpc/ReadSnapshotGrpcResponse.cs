using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using ProtoBuf;
using Schemata.Insight.Skeleton.Models;
using Schemata.Report.Foundation.Queries;
using Schemata.Transport.Grpc;
using Schemata.Transport.Grpc.Wire;

namespace Schemata.Report.Grpc;

[ProtoContract]
public sealed class ReadSnapshotGrpcResponse
{
    [ProtoMember(1)] public List<DynamicStruct> Rows { get; set; } = [];
    [ProtoMember(2)] public string? NextPageToken { get; set; }
    [ProtoMember(3)] public List<DynamicFieldDescriptor<FieldType>> Schema { get; set; } = [];

    public static ReadSnapshotGrpcResponse FromResponse(ReadSnapshotResponse response) {
        var result = new ReadSnapshotGrpcResponse { NextPageToken = response.NextPageToken };
        foreach (var field in response.Schema) result.Schema.Add(ToField(field));
        foreach (var row in response.Rows) {
            var decoded = new Dictionary<string, object?>(row.Count, StringComparer.Ordinal);
            foreach (var (key, value) in row) {
                var field = response.Schema.FirstOrDefault(field => field.Name == key);
                decoded.Add(key, value is JsonElement json ? InsightValueModel.Decode(json, field) : value);
            }
            result.Rows.Add(DynamicValueMapper.ToStruct(InsightValueModel.EncodeRow(decoded, response.Schema), InsightValueModel.Unsupported));
        }
        return result;
    }

    public static ReadSnapshotResponse ToResponse(ReadSnapshotGrpcResponse response) => new() {
        NextPageToken = response.NextPageToken,
        Schema = response.Schema.Select(FromField).ToArray(),
        Rows = response.Rows.Select(row => (IReadOnlyDictionary<string, object?>)row.Fields.ToDictionary(
            pair => pair.Key, pair => FromValue(pair.Value, response.Schema.FirstOrDefault(field => field.Name == pair.Key)))).ToList(),
    };


    private static object? FromValue(DynamicValue value, DynamicFieldDescriptor<FieldType>? field) {
        if (field?.Type == FieldType.Dynamic) return DynamicValueMapper.FromDynamic(value);
        if (value.NullValue) return null;
        if (value.ListValue is { } list) return list.Values.Select(item => FromValue(item, field?.Element ?? field)).ToList();
        if (value.StructValue is { } map) return map.Fields.ToDictionary(pair => pair.Key,
            pair => FromValue(pair.Value, field?.Children.FirstOrDefault(child => child.Name == pair.Key || child.Name == "*")));
        if (value.BoolValue is { } boolean) return boolean;
        if (value.NumberValue is { } number) return number;
        if (value.IntValue is { } integer) return field?.Type == FieldType.UInt64 ? (object)(ulong)integer : integer;
        var text = value.StringValue!;
        return field?.Type switch {
            FieldType.UInt64 => ulong.Parse(text, CultureInfo.InvariantCulture),
            FieldType.Decimal => decimal.Parse(text, CultureInfo.InvariantCulture),
            FieldType.Bytes => Convert.FromBase64String(text),
            FieldType.Guid => Guid.ParseExact(text, "D"),
            FieldType.Timestamp => DateTime.ParseExact(text, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            FieldType.DateTimeOffset => DateTimeOffset.ParseExact(text, "O", CultureInfo.InvariantCulture),
            FieldType.Duration => TimeSpan.ParseExact(text, "c", CultureInfo.InvariantCulture),
            FieldType.Char => text[0],
            _ => text,
        };
    }


    private static DynamicFieldDescriptor<FieldType> ToField(FieldDescriptor field) => new() {
        Name = field.Name, Type = field.Type, SourceAlias = field.SourceAlias, IsList = field.IsList,
        Children = field.Children.Select(ToField).ToList(),
        Element = field.Element is null ? null : ToField(field.Element),
    };

    private static FieldDescriptor FromField(DynamicFieldDescriptor<FieldType> field) => new(
        field.Name, field.Type, field.SourceAlias, field.IsList, field.Children.Select(FromField).ToImmutableArray()) {
        Element = field.Element is null ? null : FromField(field.Element),
    };
}
