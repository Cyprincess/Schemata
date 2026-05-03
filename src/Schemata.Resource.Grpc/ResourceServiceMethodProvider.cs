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

        var args   = t.GetGenericArguments();
        var method = typeof(ResourceServiceMethodProvider<TService>).GetMethod(nameof(RegisterAll), BindingFlags.Static | BindingFlags.NonPublic)!.MakeGenericMethod(args[0], args[1], args[2], args[3]);

        Registrar = (Action<ServiceMethodProviderContext<TService>, ResourceBinderConfiguration>)Delegate.CreateDelegate(typeof(Action<ServiceMethodProviderContext<TService>, ResourceBinderConfiguration>), method);
    }

    public ResourceServiceMethodProvider(ResourceBinderConfiguration config) { _config = config; }

    #region IServiceMethodProvider<TService> Members

    void IServiceMethodProvider<TService>.OnServiceMethodDiscovery(ServiceMethodProviderContext<TService> context) {
        Registrar?.Invoke(context, _config);
    }

    #endregion

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

        // Empty metadata array — grpc-dotnet does not inject authorization metadata at
        // the method provider level; that is handled by the endpoint builder in
        // SchemataGrpcResourceFeature.
        var metadata = Array.Empty<object>();

        context.AddUnaryMethod(
            new Method<ListRequest, ListResult<TSummary>>(MethodType.Unary, service, $"List{descriptor.Plural}", CreateMarshaller<ListRequest>(model), CreateMarshaller<ListResult<TSummary>>(model)), metadata,
            async (svc, req, _) => {
                var rs = (IResourceService<TEntity, TRequest, TDetail, TSummary>)svc;
                return await rs.ListAsync(req);
            });

        context.AddUnaryMethod(
            new Method<GetRequest, TDetail>(MethodType.Unary, service, $"Get{descriptor.Singular}", CreateMarshaller<GetRequest>(model), CreateMarshaller<TDetail>(model)),
            metadata, async (svc, req, _) => {
                var rs = (IResourceService<TEntity, TRequest, TDetail, TSummary>)svc;
                return await rs.GetAsync(req);
            });

        context.AddUnaryMethod(
            new Method<TRequest, TDetail>(MethodType.Unary, service, $"Create{descriptor.Singular}", CreateMarshaller<TRequest>(model), CreateMarshaller<TDetail>(model)),
            metadata, async (svc, req, _) => {
                var rs = (IResourceService<TEntity, TRequest, TDetail, TSummary>)svc;
                return await rs.CreateAsync(req);
            });

        context.AddUnaryMethod(
            new Method<TRequest, TDetail>(MethodType.Unary, service, $"Update{descriptor.Singular}", CreateMarshaller<TRequest>(model), CreateMarshaller<TDetail>(model)),
            metadata, async (svc, req, _) => {
                var rs = (IResourceService<TEntity, TRequest, TDetail, TSummary>)svc;
                return await rs.UpdateAsync(req);
            });

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
