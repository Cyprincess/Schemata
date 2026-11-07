using Grpc.AspNetCore.Server.Model;
using Microsoft.AspNetCore.Routing;
using Schemata.Quota.Grpc;

namespace Microsoft.AspNetCore.Builder;

public static class QuotaGrpcEndpointBuilderExtensions
{
    public static GrpcServiceEndpointConventionBuilder MapSchemataQuotaGrpc(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapGrpcService<QuotaGrpcService>();
}
