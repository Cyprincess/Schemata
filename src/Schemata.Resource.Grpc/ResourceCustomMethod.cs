using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Grpc.AspNetCore.Server.Model;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Resource.Foundation;
using Schemata.Resource.Grpc.Internal;

namespace Schemata.Resource.Grpc;

/// <summary>
///     Registers AIP-136 custom method unary RPCs on a code-first gRPC
///     resource service. The RPC name follows the AIP-136 convention
///     <c>{Verb}{Singular}</c> and is exposed on the resource's existing
///     service so callers see verbs and CRUD on the same surface
///     per <seealso href="https://google.aip.dev/136">AIP-136: Custom methods</seealso>.
/// </summary>
internal static class ResourceCustomMethod
{
    private static readonly MethodInfo RegisterTypedMethod = typeof(ResourceCustomMethod)
        .GetMethod(nameof(RegisterTyped), BindingFlags.NonPublic | BindingFlags.Static)!;

    public static void Register<TService>(
        ServiceMethodProviderContext<TService> context,
        ResourceBinderConfiguration            config,
        SchemataResourceOptions                options
    ) where TService : class {
        var serviceType = typeof(TService);
        if (!serviceType.IsGenericType || serviceType.GetGenericTypeDefinition() != typeof(ResourceService<,,,>)) {
            return;
        }

        var entity = serviceType.GetGenericArguments()[0];
        if (!options.Methods.TryGetValue(entity.TypeHandle, out var methods) || methods.Count == 0) {
            return;
        }

        if (options.Resources.TryGetValue(entity.TypeHandle, out var resourceAttr)
         && resourceAttr.Endpoints is { Count: > 0 } endpoints
         && endpoints.All(e => e != GrpcResourceAttribute.Name)) {
            return;
        }

        var descriptor = ResourceNameDescriptor.ForType(entity);
        var service    = GrpcResourceNaming.ServiceFullName(entity, descriptor);

        foreach (var method in methods) {
            var handlerInterface = FindHandlerInterface(method.Handler);
            if (handlerInterface is null) {
                continue;
            }

            var arguments = handlerInterface.GetGenericArguments();
            var request   = arguments[1];
            var response  = arguments[2];

            var rpcName = GrpcResourceNaming.CustomMethodName(descriptor, method.Verb);

            var generic = RegisterTypedMethod.MakeGenericMethod(typeof(TService), entity, request, response);
            generic.Invoke(null, [context, config, service, rpcName, method.Verb, method.Handler]);
        }
    }

    private static void RegisterTyped<TService, TEntity, TRequest, TResponse>(
        ServiceMethodProviderContext<TService> context,
        ResourceBinderConfiguration            config,
        string                                 service,
        string                                 rpcName,
        string                                 verb,
        Type                                   handlerType
    )
        where TService : class
        where TEntity : class, ICanonicalName
        where TRequest : class, ICanonicalName
        where TResponse : class, ICanonicalName {
        var rpc = new Method<TRequest, TResponse>(
            MethodType.Unary,
            service,
            rpcName,
            GrpcMarshallers.Create<TRequest>(config.Model),
            GrpcMarshallers.Create<TResponse>(config.Model));

        context.AddUnaryMethod(rpc, [], (_, request, callContext) => InvokeAsync<TEntity, TRequest, TResponse>(request, callContext, verb, handlerType));
    }

    private static async Task<TResponse> InvokeAsync<TEntity, TRequest, TResponse>(
        TRequest          request,
        ServerCallContext ctx,
        string            verb,
        Type              handlerType
    )
        where TEntity : class, ICanonicalName
        where TRequest : class, ICanonicalName
        where TResponse : class, ICanonicalName {
        var http      = ctx.GetHttpContext();
        var sp        = http.RequestServices;
        var operation = sp.GetRequiredService<ResourceMethodOperationHandler<TEntity, TRequest, TResponse>>();
        var handler   = (IResourceMethodHandler<TEntity, TRequest, TResponse>)sp.GetRequiredService(handlerType);

        var name = request.CanonicalName;

        return await operation.InvokeAsync(handler, verb, name, request, http.User, ctx.CancellationToken);
    }

    private static Type? FindHandlerInterface(Type handler) {
        foreach (var iface in handler.GetInterfaces()) {
            if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IResourceMethodHandler<,,>)) {
                return iface;
            }
        }
        return null;
    }
}
