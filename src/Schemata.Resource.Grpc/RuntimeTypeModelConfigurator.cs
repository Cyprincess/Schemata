using ProtoBuf;
using ProtoBuf.Meta;
using Schemata.Abstractions.Resource;
using Schemata.Core.Building;
using Schemata.Resource.Foundation;
using Schemata.Resource.Grpc.Runtime;
using Schemata.Transport.Grpc.Proto;

namespace Schemata.Resource.Grpc;

/// <summary>
///     Builds protobuf-net runtime models for resource gRPC services.
/// </summary>
internal static class RuntimeTypeModelConfigurator
{
    /// <summary>
    ///     Creates a runtime model containing standard resource messages and registered gRPC-enabled resource DTOs.
    /// </summary>
    /// <param name="registry">The registered resources.</param>
    /// <returns>The configured protobuf-net runtime model.</returns>
    public static RuntimeTypeModel Configure(ResourceRegistry registry) {
        var model = RuntimeTypeModel.Create();

        model.DefaultCompatibilityLevel = CompatibilityLevel.Level300;

        SchemataProtoModelConfigurator.ConfigureType(model, typeof(ListRequest));
        SchemataProtoModelConfigurator.ConfigureType(model, typeof(GetRequest));
        SchemataProtoModelConfigurator.ConfigureType(model, typeof(DeleteRequest));

        foreach (var resource in registry.Resources) {
            if (!GrpcResourceHelper.IsGrpcEnabled(resource)) {
                continue;
            }

            SchemataProtoModelConfigurator.ConfigureType(model, resource.Request);
            SchemataProtoModelConfigurator.ConfigureType(model, resource.Detail);
            SchemataProtoModelConfigurator.ConfigureType(model, resource.Summary);
            SchemataProtoModelConfigurator.ConfigureListResultType(model, resource.Summary!);
        }

        foreach (var resource in registry.Resources) {
            if (!GrpcResourceHelper.IsGrpcEnabled(resource)) {
                continue;
            }

            foreach (var method in registry.GetMethods(resource.Entity)) {
                var descriptor = ResourceMethodHandlerHelper.Describe(resource.Entity, method.Handler);
                if (descriptor is null) {
                    continue;
                }

                SchemataProtoModelConfigurator.ConfigureType(model, descriptor.Request);
                SchemataProtoModelConfigurator.ConfigureType(model, descriptor.Response);
            }
        }

        return model;
    }
}
