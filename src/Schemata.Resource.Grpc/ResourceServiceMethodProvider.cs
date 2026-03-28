using System;
using System.Reflection;
using Grpc.AspNetCore.Server.Model;
using Grpc.Core;
using ProtoBuf.Meta;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Empty = Google.Protobuf.WellKnownTypes.Empty;

namespace Schemata.Resource.Grpc;

/// <summary>
///     Discovers and registers gRPC methods for <see cref="ResourceService{TEntity,TRequest,TDetail,TSummary}" />
///     using standard grpc-dotnet APIs with protobuf-net serialization.
///     Registered as an open generic <c>IServiceMethodProvider&lt;&gt;</c>; silently skips any
///     <typeparamref name="TService" /> that is not a closed <c>ResourceService&lt;,,,&gt;</c>.
/// </summary>
internal sealed class ResourceServiceMethodProvider<TService> : IServiceMethodProvider<TService>
    where TService : class
{
    private static readonly Action<ServiceMethodProviderContext<TService>, ResourceBinderConfiguration>? Registrar;

    private static readonly Marshaller<Empty> EmptyMarshaller = new((_, ctx) => ctx.Complete(), _ => new());

    private readonly ResourceBinderConfiguration _config;

    static ResourceServiceMethodProvider() {
        var t = typeof(TService);
        if (!t.IsGenericType || t.GetGenericTypeDefinition() != typeof(ResourceService<,,,>)) {
            return;
        }

        var args = t.GetGenericArguments();
        var method = typeof(ResourceServiceMethodProvider<TService>).GetMethod(nameof(RegisterAll), BindingFlags.Static | BindingFlags.NonPublic)!.MakeGenericMethod(args[0], args[1], args[2], args[3]);

        Registrar = (Action<ServiceMethodProviderContext<TService>, ResourceBinderConfiguration>)Delegate.CreateDelegate(typeof(Action<ServiceMethodProviderContext<TService>, ResourceBinderConfiguration>), method);
    }

    public ResourceServiceMethodProvider(ResourceBinderConfiguration config) { _config = config; }

    #region IServiceMethodProvider<TService> Members

    void IServiceMethodProvider<TService>.OnServiceMethodDiscovery(ServiceMethodProviderContext<TService> context) {
        Registrar?.Invoke(context, _config);
    }

    #endregion

    // Called via reflection from the static constructor to cross the generic boundary.
    // ReSharper disable once UnusedMember.Local
    private static void RegisterAll<TEntity, TRequest, TDetail, TSummary>(
        ServiceMethodProviderContext<TService> context,
        ResourceBinderConfiguration            config
    )
        where TEntity : class, ICanonicalName
        where TRequest : class, ICanonicalName
        where TDetail : class, ICanonicalName
        where TSummary : class, ICanonicalName {
        var model = config.Model;
        var descriptor = ResourceNameDescriptor.ForType(typeof(TEntity));
        var package = descriptor.Package ?? typeof(TEntity).Namespace;
        var service = package is not null ? $"{package}.{descriptor.Singular}Service" : $"{descriptor.Singular}Service";

        var metadata = Array.Empty<object>();

        // List
        context.AddUnaryMethod(
            new Method<ListRequest, ListResult<TSummary>>(MethodType.Unary, service, $"List{descriptor.Plural}", CreateMarshaller<ListRequest>(model), CreateMarshaller<ListResult<TSummary>>(model)), metadata,
            async (svc, req, _) => {
                var rs = (IResourceService<TEntity, TRequest, TDetail, TSummary>)svc;
                return await rs.ListAsync(req);
            });

        // Get
        context.AddUnaryMethod(
            new Method<GetRequest, TDetail>(MethodType.Unary, service, $"Get{descriptor.Singular}", CreateMarshaller<GetRequest>(model), CreateMarshaller<TDetail>(model)),
            metadata, async (svc, req, _) => {
                var rs = (IResourceService<TEntity, TRequest, TDetail, TSummary>)svc;
                return await rs.GetAsync(req);
            });

        // Create
        context.AddUnaryMethod(
            new Method<TRequest, TDetail>(MethodType.Unary, service, $"Create{descriptor.Singular}", CreateMarshaller<TRequest>(model), CreateMarshaller<TDetail>(model)),
            metadata, async (svc, req, _) => {
                var rs = (IResourceService<TEntity, TRequest, TDetail, TSummary>)svc;
                return await rs.CreateAsync(req);
            });

        // Update
        context.AddUnaryMethod(
            new Method<TRequest, TDetail>(MethodType.Unary, service, $"Update{descriptor.Singular}", CreateMarshaller<TRequest>(model), CreateMarshaller<TDetail>(model)),
            metadata, async (svc, req, _) => {
                var rs = (IResourceService<TEntity, TRequest, TDetail, TSummary>)svc;
                return await rs.UpdateAsync(req);
            });

        // Delete
        context.AddUnaryMethod(
            new Method<DeleteRequest, Empty>(MethodType.Unary, service, $"Delete{descriptor.Singular}", CreateMarshaller<DeleteRequest>(model), EmptyMarshaller), metadata,
            async (svc, req, _) => {
                var rs = (IResourceService<TEntity, TRequest, TDetail, TSummary>)svc;
                await rs.DeleteAsync(req);
                return new();
            });
    }

    private static Marshaller<T> CreateMarshaller<T>(RuntimeTypeModel model) {
        return new((value, ctx) => {
            model.Serialize(ctx.GetBufferWriter(), value);
            ctx.Complete();
        }, ctx => model.Deserialize<T>(ctx.PayloadAsReadOnlySequence()));
    }
}
