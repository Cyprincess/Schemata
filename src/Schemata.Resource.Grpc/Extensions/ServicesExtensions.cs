using System;
using Grpc.AspNetCore.Server;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProtoBuf.Grpc.Configuration;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///     Extension methods for registering code-first gRPC services with protobuf-net
///     support and a <see cref="SimpleRpcExceptionsInterceptor" /> that enables
///     gRPC status propagation for RPC-level exceptions.
/// </summary>
public static class ServicesExtensions
{
    /// <summary>
    ///     Adds gRPC server services with code-first (protobuf-net) support and
    ///     the <see cref="SimpleRpcExceptionsInterceptor" />.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection" />.</param>
    /// <param name="configureOptions">Optional callback to configure <see cref="GrpcServiceOptions" />.</param>
    /// <returns>The <see cref="IGrpcServerBuilder" /> for further configuration.</returns>
    public static IGrpcServerBuilder AddCodeFirstGrpc(
        this IServiceCollection     services,
        Action<GrpcServiceOptions>? configureOptions
    ) {
        var builder = configureOptions == null ? services.AddGrpc() : services.AddGrpc(configureOptions);
        services.TryAddSingleton(SimpleRpcExceptionsInterceptor.Instance);
        return builder;
    }
}
