using System;
using System.Linq;
using ProtoBuf;
using ProtoBuf.Meta;
using Schemata.Abstractions.Resource;
using Schemata.Resource.Foundation;
using Schemata.Transport.Grpc.Proto;

namespace Schemata.Resource.Grpc;

internal static class RuntimeTypeModelConfigurator
{
    public static RuntimeTypeModel Configure(SchemataResourceOptions options) {
        var model = RuntimeTypeModel.Create();

        model.DefaultCompatibilityLevel = CompatibilityLevel.Level300;

        SchemataProtoModelConfigurator.ConfigureType(model, typeof(ListRequest));
        SchemataProtoModelConfigurator.ConfigureType(model, typeof(GetRequest));
        SchemataProtoModelConfigurator.ConfigureType(model, typeof(DeleteRequest));

        foreach (var (_, resource) in options.Resources) {
            if (!IsGrpcEnabled(resource)) {
                continue;
            }

            SchemataProtoModelConfigurator.ConfigureType(model, resource.Request);
            SchemataProtoModelConfigurator.ConfigureType(model, resource.Detail);
            SchemataProtoModelConfigurator.ConfigureType(model, resource.Summary);
            SchemataProtoModelConfigurator.ConfigureListResultType(model, resource.Summary!);
        }

        foreach (var (handle, methods) in options.Methods) {
            if (!options.Resources.TryGetValue(handle, out var resource) || !IsGrpcEnabled(resource)) {
                continue;
            }

            foreach (var method in methods) {
                var iface = FindHandlerInterface(method.Handler);
                if (iface is null) {
                    continue;
                }

                var arguments = iface.GetGenericArguments();
                SchemataProtoModelConfigurator.ConfigureType(model, arguments[1]);
                SchemataProtoModelConfigurator.ConfigureType(model, arguments[2]);
            }
        }

        return model;
    }

    private static bool IsGrpcEnabled(ResourceAttribute resource) {
        return resource.Endpoints is null
            || resource.Endpoints.Count == 0
            || resource.Endpoints.Any(e => e == GrpcResourceAttribute.Name);
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
