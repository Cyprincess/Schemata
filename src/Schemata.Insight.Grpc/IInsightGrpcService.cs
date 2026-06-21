using System.Threading.Tasks;
using ProtoBuf.Grpc;
using ProtoBuf.Grpc.Configuration;

namespace Schemata.Insight.Grpc;

/// <summary>
///     Code-first gRPC contract for the federated read query, mirroring the HTTP
///     <c>POST /v1/insight:query</c> endpoint; implemented by <see cref="InsightGrpcService" />.
/// </summary>
[Service]
public interface IInsightGrpcService
{
    /// <summary>Plans and executes a federated read query.</summary>
    /// <param name="request">The query request.</param>
    /// <param name="context">The gRPC call context.</param>
    /// <returns>The paginated query result.</returns>
    [Operation]
    ValueTask<QueryInsightGrpcResponse> QueryAsync(QueryInsightGrpcRequest request, CallContext context = default);
}
