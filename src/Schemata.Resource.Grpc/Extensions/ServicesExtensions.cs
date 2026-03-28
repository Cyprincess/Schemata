// Some code is borrowed from protobuf-net
// https://github.com/protobuf-net/protobuf-net.Grpc/blob/1a3a67b1ed7d48997a99c747c936cd7d0416a9d7/src/protobuf-net.Grpc.AspNetCore/ServicesExtensions.cs
// The borrowed code is licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)

using System;
using Grpc.AspNetCore.Server;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProtoBuf.Grpc.Configuration;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///     Extension methods for registering code-first gRPC services.
/// </summary>
public static class ServicesExtensions
{
    /// <summary>
    ///     Adds gRPC services with code-first (protobuf-net) support and exception interception.
    /// </summary>
    /// <param name="services">The DI service collection.</param>
    /// <param name="configureOptions">Optional callback to configure gRPC service options.</param>
    /// <returns>The gRPC server builder for further configuration.</returns>
    public static IGrpcServerBuilder AddCodeFirstGrpc(
        this IServiceCollection     services,
        Action<GrpcServiceOptions>? configureOptions
    ) {
        var builder = configureOptions == null ? services.AddGrpc() : services.AddGrpc(configureOptions);
        services.TryAddSingleton(SimpleRpcExceptionsInterceptor.Instance);
        return builder;
    }
}
