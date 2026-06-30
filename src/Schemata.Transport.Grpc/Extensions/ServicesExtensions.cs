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
    /// <summary>
    ///     Adds gRPC with code-first protobuf-net support.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Optional gRPC server options configuration.</param>
    /// <returns>The gRPC server builder.</returns>
    public static IGrpcServerBuilder AddCodeFirstGrpc(
        this IServiceCollection     services,
        Action<GrpcServiceOptions>? configureOptions
    ) {
        var builder = configureOptions is null ? services.AddGrpc() : services.AddGrpc(configureOptions);
        services.TryAddSingleton(SimpleRpcExceptionsInterceptor.Instance);
        return builder;
    }
}
