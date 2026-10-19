using Grpc.AspNetCore.Server.Model;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Schemata.Push.Grpc;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>Endpoint registration for the Push gRPC control plane.</summary>
public static class PushGrpcEndpointBuilderExtensions
{
    /// <summary>Maps the Push control-plane gRPC service.</summary>
    public static GrpcServiceEndpointConventionBuilder MapSchemataPushGrpc(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapGrpcService<PushControlGrpcService>();
}
