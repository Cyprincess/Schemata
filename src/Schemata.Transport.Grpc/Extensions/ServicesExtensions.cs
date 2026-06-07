using System;
using Grpc.AspNetCore.Server;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProtoBuf.Grpc.Configuration;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///     Code-first gRPC registration with protobuf-net and the
///     <see cref="SimpleRpcExceptionsInterceptor" />.
/// </summary>
public static class ServicesExtensions
{
    /// <summary>Adds gRPC with code-first (protobuf-net) support.</summary>
    public static IGrpcServerBuilder AddCodeFirstGrpc(
        this IServiceCollection     services,
        Action<GrpcServiceOptions>? configureOptions
    ) {
        var builder = configureOptions == null ? services.AddGrpc() : services.AddGrpc(configureOptions);
        services.TryAddSingleton(SimpleRpcExceptionsInterceptor.Instance);
        return builder;
    }
}
