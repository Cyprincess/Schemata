using System;
using System.Collections.Generic;
using Google.Protobuf.Reflection;
using Grpc.AspNetCore.Server.Model;
using Grpc.Core;
using ProtoBuf;
using ProtoBuf.Meta;
using Schemata.Messaging.Skeleton;
using Schemata.Transport.Grpc.Proto;

namespace Schemata.Transport.Grpc;

internal sealed class StreamDescriptorRegistry
{
    public RuntimeTypeModel Model { get; } = CreateModel();
    private readonly Dictionary<(string Service, string Method), GrpcMethodSchema> _methods = [];

    private static RuntimeTypeModel CreateModel() {
        var model = RuntimeTypeModel.Create();
        model.DefaultCompatibilityLevel = CompatibilityLevel.Level300;
        return model;
    }

    public void Add(string serviceName, string methodName, Type request, Type item) {
        lock (Model) {
            SchemataProtoModelConfigurator.ConfigureType(Model, request);
            SchemataProtoModelConfigurator.ConfigureType(Model, item);
            _methods[(serviceName, methodName)] = new(serviceName, methodName, request, item, MethodType.ServerStreaming);
        }
    }

    public IReadOnlyList<ServiceDescriptor> GetDescriptors() {
        lock (Model) {
            return GrpcSchema.Build(Model, _methods.Values);
        }
    }
}

internal sealed class StreamDescriptorContributor(StreamDescriptorRegistry registry) : IGrpcServiceDescriptorContributor
{
    public IReadOnlyList<ServiceDescriptor> GetServiceDescriptors(IServiceProvider serviceProvider) => registry.GetDescriptors();
}

internal sealed class StreamRegistration<TRequest, TItem> : IServiceMethodProvider<StreamService<TRequest, TItem>>
    where TRequest : class, IStreamRequest<TItem> where TItem : class
{
    private readonly Method<TRequest, TItem> _method;

    public StreamRegistration(StreamDescriptorRegistry registry, string serviceName, string methodName) {
        registry.Add(serviceName, methodName, typeof(TRequest), typeof(TItem));
        _method = new(MethodType.ServerStreaming, serviceName, methodName,
            GrpcMarshallers.Create<TRequest>(registry.Model), GrpcMarshallers.Create<TItem>(registry.Model));
    }

    public void OnServiceMethodDiscovery(ServiceMethodProviderContext<StreamService<TRequest, TItem>> context) =>
        context.AddServerStreamingMethod(_method, [],
            static (service, request, writer, call) => service.InvokeAsync(request, writer, call));
}
