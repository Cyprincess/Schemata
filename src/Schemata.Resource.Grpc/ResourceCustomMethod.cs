using System.Reflection;
using System.Threading.Tasks;
using Grpc.AspNetCore.Server.Model;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Entities;
using Schemata.Common;
using Schemata.Core.Building;
using Schemata.Resource.Foundation;
using Schemata.Messaging.Skeleton;
using Schemata.Resource.Grpc.Runtime;
using Schemata.Transport.Grpc;

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

    /// <summary>
    ///     Registers custom-method RPCs for the supplied resource service type.
    /// </summary>
    /// <typeparam name="TService">The closed resource service type.</typeparam>
    /// <param name="context">The gRPC service method discovery context.</param>
    /// <param name="config">The resource gRPC binder configuration.</param>
    /// <param name="registry">The registered resources.</param>
    public static void Register<TService>(
        ServiceMethodProviderContext<TService> context,
        ResourceBinderConfiguration            config,
        ResourceRegistry                      registry
    ) where TService : class {
        var serviceType = typeof(TService);
        if (!serviceType.IsGenericType || serviceType.GetGenericTypeDefinition() != typeof(ResourceService<,,,>)) {
            return;
        }

        var entity  = serviceType.GetGenericArguments()[0];
        var methods = registry.GetMethods(entity);
        if (methods.Count == 0) {
            return;
        }

        if (registry.GetResource(entity) is { } resourceAttr && !GrpcResourceHelper.IsGrpcEnabled(resourceAttr)) {
            return;
        }

        var descriptor = ResourceNameDescriptor.ForType(entity);
        var service    = GrpcResourceNaming.ServiceFullName(entity, descriptor);

        foreach (var method in methods) {
            var methodDescriptor = ResourceMethodHandlerHelper.Describe(entity, method.Handler);
            if (methodDescriptor is null) {
                continue;
            }

            var rpcName = GrpcResourceNaming.CustomMethodName(descriptor, method.Verb);

            var generic = RegisterTypedMethod.MakeGenericMethod(
                typeof(TService), entity, methodDescriptor.Request, methodDescriptor.Response);
            generic.Invoke(null, [context, config, service, rpcName, method.Verb]);
        }
    }

    private static void RegisterTyped<TService, TEntity, TRequest, TResponse>(
        ServiceMethodProviderContext<TService> context,
        ResourceBinderConfiguration            config,
        string                                 service,
        string                                 rpcName,
        string                                 verb
    )
        where TService : class
        where TEntity : class, ICanonicalName
        where TRequest : class, IRequest<TResponse>, IRequestPrincipal
        where TResponse : class, ICanonicalName {
        var rpc = new Method<TRequest, TResponse>(
            MethodType.Unary,
            service,
            rpcName,
            GrpcMarshallers.Create<TRequest>(config.Model),
            GrpcMarshallers.Create<TResponse>(config.Model));

        context.AddUnaryMethod(rpc, [], (_, request, callContext) => InvokeAsync<TEntity, TRequest, TResponse>(request, callContext, verb));
    }

    private static async Task<TResponse> InvokeAsync<TEntity, TRequest, TResponse>(
        TRequest          request,
        ServerCallContext ctx,
        string            verb
    )
        where TEntity : class, ICanonicalName
        where TRequest : class, IRequest<TResponse>, IRequestPrincipal
        where TResponse : class, ICanonicalName {
        var http      = ctx.GetHttpContext();
        var sp        = http.RequestServices;
        var operation = sp.GetRequiredService<ResourceMethodOperationHandler<TEntity, TRequest, TResponse>>();
        var name      = (request as ICanonicalName)?.CanonicalName;

        return await operation.InvokeAsync(verb, name, request, http.User, ctx.CancellationToken);
    }
}
