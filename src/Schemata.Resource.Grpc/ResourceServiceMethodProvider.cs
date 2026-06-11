using System;
using System.Reflection;
using Grpc.AspNetCore.Server.Model;
using Grpc.Core;
using Microsoft.Extensions.Options;
using ProtoBuf.Meta;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Resource.Foundation;
using Empty = Google.Protobuf.WellKnownTypes.Empty;

namespace Schemata.Resource.Grpc;

internal sealed class ResourceServiceMethodProvider<TService> : IServiceMethodProvider<TService>
    where TService : class
{
    private static readonly Action<ServiceMethodProviderContext<TService>, ResourceBinderConfiguration, SchemataResourceOptions>? Registrar;

    private static readonly Marshaller<Empty> EmptyMarshaller = new((_, ctx) => ctx.Complete([]), _ => new());

    private readonly ResourceBinderConfiguration       _config;
    private readonly IOptions<SchemataResourceOptions> _options;

    static ResourceServiceMethodProvider() {
        var t = typeof(TService);
        if (!t.IsGenericType || t.GetGenericTypeDefinition() != typeof(ResourceService<,,,>)) {
            return;
        }

        var args   = t.GetGenericArguments();
        var method = typeof(ResourceServiceMethodProvider<TService>).GetMethod(nameof(RegisterAll), BindingFlags.Static | BindingFlags.NonPublic)!.MakeGenericMethod(args[0], args[1], args[2], args[3]);

        Registrar = (Action<ServiceMethodProviderContext<TService>, ResourceBinderConfiguration, SchemataResourceOptions>)Delegate.CreateDelegate(typeof(Action<ServiceMethodProviderContext<TService>, ResourceBinderConfiguration, SchemataResourceOptions>), method);
    }

    public ResourceServiceMethodProvider(
        ResourceBinderConfiguration       config,
        IOptions<SchemataResourceOptions> options
    ) {
        _config  = config;
        _options = options;
    }

    #region IServiceMethodProvider<TService> Members

    void IServiceMethodProvider<TService>.OnServiceMethodDiscovery(ServiceMethodProviderContext<TService> context) {
        Registrar?.Invoke(context, _config, _options.Value);
        ResourceCustomMethod.Register(context, _config, _options.Value);
    }

    #endregion

    private static void RegisterAll<TEntity, TRequest, TDetail, TSummary>(
        ServiceMethodProviderContext<TService> context,
        ResourceBinderConfiguration            config,
        SchemataResourceOptions                options
    )
        where TEntity : class, ICanonicalName
        where TRequest : class, ICanonicalName
        where TDetail : class, ICanonicalName
        where TSummary : class, ICanonicalName {
        var model      = config.Model;
        var descriptor = ResourceNameDescriptor.ForType<TEntity>();
        var package    = descriptor.Package ?? typeof(TEntity).Namespace;
        var service    = package is not null ? $"{package}.{descriptor.Singular}Service" : $"{descriptor.Singular}Service";

        var allowed = options.Resources.TryGetValue(typeof(TEntity).TypeHandle, out var resource)
                          ? resource.Operations
                          : null;

        bool IsAllowed(Operations verb) {
            return allowed is null || Array.IndexOf(allowed, verb) >= 0;
        }

        var metadata = Array.Empty<object>();

        if (IsAllowed(Operations.List)) {
            context.AddUnaryMethod(
                new Method<ListRequest, ListResultBase<TSummary>>(MethodType.Unary, service, $"List{descriptor.Plural}", CreateMarshaller<ListRequest>(model), CreateMarshaller<ListResultBase<TSummary>>(model)), metadata,
                async (svc, req, ctx) => {
                    var rs = (IResourceService<TEntity, TRequest, TDetail, TSummary>)svc;
                    return await rs.ListAsync(req, new(svc, ctx));
                });
        }

        if (IsAllowed(Operations.Get)) {
            context.AddUnaryMethod(
                new Method<GetRequest, TDetail>(MethodType.Unary, service, $"Get{descriptor.Singular}", CreateMarshaller<GetRequest>(model), CreateMarshaller<TDetail>(model)),
                metadata, async (svc, req, ctx) => {
                    var rs = (IResourceService<TEntity, TRequest, TDetail, TSummary>)svc;
                    return await rs.GetAsync(req, new(svc, ctx));
                });
        }

        if (IsAllowed(Operations.Create)) {
            context.AddUnaryMethod(
                new Method<TRequest, TDetail>(MethodType.Unary, service, $"Create{descriptor.Singular}", CreateMarshaller<TRequest>(model), CreateMarshaller<TDetail>(model)),
                metadata, async (svc, req, ctx) => {
                    var rs = (IResourceService<TEntity, TRequest, TDetail, TSummary>)svc;
                    return await rs.CreateAsync(req, new(svc, ctx));
                });
        }

        if (IsAllowed(Operations.Update)) {
            context.AddUnaryMethod(
                new Method<TRequest, TDetail>(MethodType.Unary, service, $"Update{descriptor.Singular}", CreateMarshaller<TRequest>(model), CreateMarshaller<TDetail>(model)),
                metadata, async (svc, req, ctx) => {
                    var rs = (IResourceService<TEntity, TRequest, TDetail, TSummary>)svc;
                    return await rs.UpdateAsync(req, new(svc, ctx));
                });
        }

        if (IsAllowed(Operations.Delete)) {
            // Soft-deletable resources respond with the updated resource per AIP-164;
            // hard-deletable resources respond with Empty per AIP-135.
            if (typeof(ISoftDelete).IsAssignableFrom(typeof(TEntity))) {
                context.AddUnaryMethod(
                    new Method<DeleteRequest, TDetail>(MethodType.Unary, service, $"Delete{descriptor.Singular}", CreateMarshaller<DeleteRequest>(model), CreateMarshaller<TDetail>(model)), metadata,
                    async (svc, req, ctx) => {
                        var rs = (IResourceService<TEntity, TRequest, TDetail, TSummary>)svc;
                        return (await rs.DeleteAsync(req, new(svc, ctx)))!;
                    });
            } else {
                context.AddUnaryMethod(
                    new Method<DeleteRequest, Empty>(MethodType.Unary, service, $"Delete{descriptor.Singular}", CreateMarshaller<DeleteRequest>(model), EmptyMarshaller), metadata,
                    async (svc, req, ctx) => {
                        var rs = (IResourceService<TEntity, TRequest, TDetail, TSummary>)svc;
                        await rs.DeleteAsync(req, new(svc, ctx));
                        return new();
                    });
            }
        }
    }

    private static Marshaller<T> CreateMarshaller<T>(RuntimeTypeModel model) {
        return new((value, ctx) => {
            model.Serialize(ctx.GetBufferWriter(), value);
            ctx.Complete();
        }, ctx => model.Deserialize<T>(ctx.PayloadAsReadOnlySequence()));
    }
}
