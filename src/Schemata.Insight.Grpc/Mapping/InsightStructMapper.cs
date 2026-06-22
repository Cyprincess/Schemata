using System;
using System.Collections.Generic;
using System.Linq;
using Schemata.Insight.Skeleton;

namespace Schemata.Insight.Grpc;

/// <summary>
///     Maps between the gRPC edge messages and the protobuf-free core wire types: the request graph
///     in, and the dynamic dictionary rows out as <see cref="InsightStruct" /> trees.
/// </summary>
public static class InsightStructMapper
{
    /// <summary>Maps a gRPC request message to the core request.</summary>
    public static QueryInsightRequest ToRequest(QueryInsightGrpcRequest message) {
        var request = new QueryInsightRequest {
            PageSize  = message.PageSize,
            Skip      = message.Skip,
            PageToken = message.PageToken,
            Language  = message.Language,
        };

        foreach (var source in message.Sources) {
            request.Sources.Add(new(source.Alias, source.Name));
        }

        foreach (var join in message.Joins) {
            request.Joins.Add(new(join.Left, join.Right, join.Kind, ToExpression(join.On)));
        }

        foreach (var transformation in message.Transformations) {
            request.Transformations.Add(ToTransformation(transformation));
        }

        foreach (var selection in message.Selections) {
            request.Selections.Add(ToSelection(selection));
        }

        return request;
    }

    /// <summary>Maps the core response to a gRPC response message.</summary>
    public static QueryInsightGrpcResponse ToResponse(QueryInsightResponse response) {
        var message = new QueryInsightGrpcResponse {
            NextPageToken = response.NextPageToken,
            TotalSize     = response.TotalSize,
        };

        foreach (var row in response.Rows) {
            message.Rows.Add(ToStruct(row));
        }

        foreach (var field in response.Schema) {
            message.Schema.Add(ToFieldDescriptor(field));
        }

        foreach (var unreachable in response.Unreachable) {
            message.Unreachable.Add(unreachable);
        }

        return message;
    }

    /// <summary>Maps a dictionary row to a dynamic struct.</summary>
    public static InsightStruct ToStruct(IReadOnlyDictionary<string, object?> row) {
        var result = new InsightStruct();
        foreach (var (key, value) in row) {
            result.Fields[key] = ToValue(value);
        }

        return result;
    }

    private static InsightValue ToValue(object? value) {
        return value switch {
            null        => new() { NullValue   = true },
            string text => new() { StringValue = text },
            bool flag   => new() { BoolValue   = flag },
            byte or sbyte or short or ushort or int or uint or long
                                                                  => new() { IntValue = Convert.ToInt64(value) },
            ulong unsigned                              => new() { IntValue    = unchecked((long)unsigned) },
            float or double or decimal                  => new() { NumberValue = Convert.ToDouble(value) },
            IReadOnlyDictionary<string, object?> nested => new() { StructValue = ToStruct(nested) },
            IEnumerable<IReadOnlyDictionary<string, object?>> list => new() {
                ListValue = list.Select(item => new InsightValue { StructValue = ToStruct(item) }).ToList(),
            },
            IEnumerable<object?> items => new() { ListValue   = items.Select(ToValue).ToList() },
            var other                  => new() { StringValue = other.ToString() },
        };
    }

    private static InsightExpression ToExpression(InsightExpressionMessage message) {
        return new(message.Source, message.Language);
    }

    private static TransformationSpec ToTransformation(TransformationMessage message) {
        var spec = new TransformationSpec();

        if (message.Filter is { } filter) {
            spec.Filter = new(ToExpression(filter));
        }

        if (message.Compute is { Count: > 0 } compute) {
            spec.Compute = new(
                [..compute.Select(field => new ComputedFieldSpec(ToExpression(field.Expression), field.Alias))]);
        }

        if (message.IsGroupBy) {
            spec.GroupBy = new(
                [..message.GroupByKeys ?? []],
                [..(message.GroupByAggregations ?? []).Select(a => new AggregationSpec(a.Field, a.Function, a.Alias))]);
        }

        if (message.OrderBy is { } orderBy) {
            spec.OrderBy = new(orderBy);
        }

        if (message.Top is { } top) {
            spec.Top = new(top);
        }

        if (message.Skip is { } skip) {
            spec.Skip = new(skip);
        }

        return spec;
    }

    private static SelectionSpec ToSelection(SelectionMessage message) {
        var spec = new SelectionSpec { Field = message.Field, Alias = message.Alias };

        if (message.Expression is { } expression) {
            spec.Expression = ToExpression(expression);
        }

        foreach (var child in message.Selections ?? []) {
            spec.Selections.Add(ToSelection(child));
        }

        foreach (var transformation in message.Transformations ?? []) {
            spec.Transformations.Add(ToTransformation(transformation));
        }

        return spec;
    }

    private static FieldDescriptorMessage ToFieldDescriptor(FieldDescriptor field) {
        var message = new FieldDescriptorMessage {
            Name        = field.Name,
            Type        = field.Type,
            SourceAlias = field.SourceAlias,
            IsList      = field.IsList,
        };

        foreach (var child in field.Children) {
            message.Children.Add(ToFieldDescriptor(child));
        }

        return message;
    }
}
