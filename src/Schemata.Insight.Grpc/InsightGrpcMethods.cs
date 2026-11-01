using System.Collections.Generic;
using ProtoBuf;
using Schemata.Transport.Grpc.Proto;
using Grpc.Core;
using ProtoBuf.Meta;
using Schemata.Insight.Grpc.Wire;
using Schemata.Transport.Grpc;

namespace Schemata.Insight.Grpc;

/// <summary>
///     The wire definition of the Insight gRPC method, shared by the server method provider and gRPC
///     clients so the service and method names and the protobuf-net marshallers always match.
/// </summary>
public static class InsightGrpcMethods
{
    /// <summary>The fully qualified gRPC service name.</summary>
    public const string ServiceName = "schemata.insight.v1.InsightService";

    public static RuntimeTypeModel Model { get; } = CreateModel();

    private static RuntimeTypeModel CreateModel() {
        var model = RuntimeTypeModel.Create();
        model.DefaultCompatibilityLevel = CompatibilityLevel.Level300;
        model.Add(typeof(QueryInsightGrpcRequest), true);
        model.Add(typeof(QueryInsightGrpcResponse), true);
        return model;
    }

    /// <summary>The unary query method.</summary>
    public static readonly Method<QueryInsightGrpcRequest, QueryInsightGrpcResponse> Query = new(
        MethodType.Unary,
        ServiceName,
        "Query",
        GrpcMarshallers.Create<QueryInsightGrpcRequest>(Model),
        GrpcMarshallers.Create<QueryInsightGrpcResponse>(Model));

    public static IReadOnlyList<GrpcMethodSchema> Methods { get; } = [GrpcMethodSchema.From(Query)];
}
