using Grpc.AspNetCore.Server.Model;
using ProtoBuf.Grpc;

namespace Schemata.Insight.Grpc;

/// <summary>
///     Binds the single unary Insight query method onto the gRPC service at discovery time, using the
///     shared <see cref="InsightGrpcMethods.Query" /> definition.
/// </summary>
internal sealed class InsightServiceMethodProvider : IServiceMethodProvider<InsightGrpcService>
{
    #region IServiceMethodProvider<InsightGrpcService> Members

    void IServiceMethodProvider<InsightGrpcService>.OnServiceMethodDiscovery(
        ServiceMethodProviderContext<InsightGrpcService> context
    ) {
        context.AddUnaryMethod(
            InsightGrpcMethods.Query,
            [],
            async (service, request, call) => await service.QueryAsync(request, new(service, call)));
    }

    #endregion
}
