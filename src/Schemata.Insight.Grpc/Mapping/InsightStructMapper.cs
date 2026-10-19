using System.Linq;
using Schemata.Transport.Grpc;
using Schemata.Transport.Grpc.Wire;
using Schemata.Insight.Grpc.Wire;
using Schemata.Insight.Skeleton.Models;
using Schemata.Insight.Skeleton.Queries;

namespace Schemata.Insight.Grpc.Mapping;

/// <summary>
///     Maps between the gRPC edge messages and the protobuf-free core wire types: the request graph
///     in, and the dynamic dictionary rows out through the shared transport encoding.
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
            message.Rows.Add(DynamicValueMapper.ToStruct(InsightValueModel.EncodeRow(row, response.Schema), InsightValueModel.Unsupported));
        }

        foreach (var field in response.Schema) {
            message.Schema.Add(ToFieldDescriptor(field));
        }

        foreach (var unreachable in response.Unreachable) {
            message.Unreachable.Add(unreachable);
        }

        return message;
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


    private static DynamicFieldDescriptor<FieldType> ToFieldDescriptor(FieldDescriptor field) {
        var message = new DynamicFieldDescriptor<FieldType> {
            Name        = field.Name,
            Type        = field.Type,
            SourceAlias = field.SourceAlias,
            IsList      = field.IsList,
            Element     = field.Element is null ? null : ToFieldDescriptor(field.Element),
        };

        foreach (var child in field.Children) {
            message.Children.Add(ToFieldDescriptor(child));
        }

        return message;
    }
}
