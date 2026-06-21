using Grpc.Core;
using ProtoBuf.Meta;

namespace Schemata.Insight.Grpc;

/// <summary>
///     The wire definition of the Insight gRPC method, shared by the server method provider and gRPC
///     clients so the service and method names and the protobuf-net marshallers always match.
/// </summary>
public static class InsightGrpcMethods
{
    /// <summary>The fully qualified gRPC service name.</summary>
    public const string ServiceName = "schemata.insight.v1.InsightService";

    /// <summary>The unary query method.</summary>
    public static readonly Method<QueryInsightGrpcRequest, QueryInsightGrpcResponse> Query = new(
        MethodType.Unary,
        ServiceName,
        "Query",
        Marshaller<QueryInsightGrpcRequest>(),
        Marshaller<QueryInsightGrpcResponse>());

    private static Marshaller<T> Marshaller<T>() {
        var model = RuntimeTypeModel.Default;
        return new Marshaller<T>(
            (value, context) => {
                model.Serialize(context.GetBufferWriter(), value);
                context.Complete();
            },
            context => model.Deserialize<T>(context.PayloadAsReadOnlySequence()));
    }
}
