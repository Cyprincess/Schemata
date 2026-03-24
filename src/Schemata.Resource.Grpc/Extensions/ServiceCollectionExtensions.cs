// Some code is borrowed from protobuf-net
// https://github.com/protobuf-net/protobuf-net.Grpc/blob/1a3a67b1ed7d48997a99c747c936cd7d0416a9d7/src/protobuf-net.Grpc.AspNetCore/ServicesExtensions.cs
// The borrowed code is licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)

using System;
using Grpc.AspNetCore.Server;
using Grpc.AspNetCore.Server.Model;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using ProtoBuf.Grpc.Configuration;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for registering code-first gRPC services.
/// </summary>
public static class ServicesExtensions
{
    /// <summary>
    /// Adds gRPC services with code-first (protobuf-net) support and exception interception.
    /// </summary>
    /// <param name="services">The DI service collection.</param>
    /// <param name="configureOptions">Optional callback to configure gRPC service options.</param>
    /// <returns>The gRPC server builder for further configuration.</returns>
    public static IGrpcServerBuilder AddCodeFirstGrpc(
        this IServiceCollection     services,
        Action<GrpcServiceOptions>? configureOptions
    ) {
        var builder = configureOptions == null ? services.AddGrpc() : services.AddGrpc(configureOptions);
        services.TryAddEnumerable(ServiceDescriptor.Singleton(typeof(IServiceMethodProvider<>), typeof(CodeFirstServiceMethodProvider<>)));
        services.TryAddSingleton(SimpleRpcExceptionsInterceptor.Instance);
        return builder;
    }

    #region Nested type: Binder

    private sealed class Binder : ServerBinder
    {
        private readonly ILogger _logger;

        internal Binder(ILogger logger) { _logger = logger; }

        protected override void OnWarn(string message, object?[]? args) { _logger.LogWarning(message, args ?? []); }

        protected override void OnError(string message, object?[]? args = null) {
            _logger.LogError(message, args ?? []);
        }

        protected override bool TryBind<TService, TRequest, TResponse>(
            ServiceBindContext          bindContext,
            Method<TRequest, TResponse> method,
            MethodStub<TService>        stub
        )
            where TService : class
            where TRequest : class
            where TResponse : class {
            if (bindContext.State is not ServiceMethodProviderContext<TService> context) {
                return base.TryBind(bindContext, method, stub);
            }

            var metadata = bindContext.GetMetadata(stub.Method);
            switch (method.Type) {
                case MethodType.Unary:
                    context.AddUnaryMethod(method, metadata, stub.CreateDelegate<UnaryServerMethod<TService, TRequest, TResponse>>());
                    break;
                case MethodType.ClientStreaming:
                    context.AddClientStreamingMethod(method, metadata, stub.CreateDelegate<ClientStreamingServerMethod<TService, TRequest, TResponse>>());
                    break;
                case MethodType.ServerStreaming:
                    context.AddServerStreamingMethod(method, metadata, stub.CreateDelegate<ServerStreamingServerMethod<TService, TRequest, TResponse>>());
                    break;
                case MethodType.DuplexStreaming:
                    context.AddDuplexStreamingMethod(method, metadata, stub.CreateDelegate<DuplexStreamingServerMethod<TService, TRequest, TResponse>>());
                    break;
                default:
                    return false;
            }

            return true;
        }
    }

    #endregion

    #region Nested type: CodeFirstServiceMethodProvider

    private sealed class CodeFirstServiceMethodProvider<TService> : IServiceMethodProvider<TService>
        where TService : class
    {
        private readonly BinderConfiguration?                              _binder;
        private readonly ILogger<CodeFirstServiceMethodProvider<TService>> _logger;

        public CodeFirstServiceMethodProvider(ILoggerFactory loggerFactory, BinderConfiguration? binder = null) {
            _binder = binder;
            _logger = loggerFactory.CreateLogger<CodeFirstServiceMethodProvider<TService>>();
        }

        #region IServiceMethodProvider<TService> Members

        void IServiceMethodProvider<TService>.OnServiceMethodDiscovery(ServiceMethodProviderContext<TService> context) {
            var count = new Binder(_logger).Bind<TService>(context, _binder);
            if (count != 0) {
                _logger.Log(LogLevel.Information, "RPC services being provided by {Service}: {Count}", typeof(TService), count);
            }
        }

        #endregion
    }

    #endregion
}
