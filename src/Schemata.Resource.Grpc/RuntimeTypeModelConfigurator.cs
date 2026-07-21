using ProtoBuf;
using ProtoBuf.Meta;
using Schemata.Abstractions.Resource;
using Schemata.Resource.Foundation;
using Schemata.Resource.Grpc.Internal;
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
    /// <param name="options">The registered resource options.</param>
    /// <returns>The configured protobuf-net runtime model.</returns>
    public static RuntimeTypeModel Configure(SchemataResourceOptions options) {
        var model = RuntimeTypeModel.Create();

        model.DefaultCompatibilityLevel = CompatibilityLevel.Level300;

        SchemataProtoModelConfigurator.ConfigureType(model, typeof(ListRequest));
        SchemataProtoModelConfigurator.ConfigureType(model, typeof(GetRequest));
        SchemataProtoModelConfigurator.ConfigureType(model, typeof(DeleteRequest));

        foreach (var (_, resource) in options.Resources) {
            if (!GrpcResourceHelper.IsGrpcEnabled(resource)) {
                continue;
            }

            SchemataProtoModelConfigurator.ConfigureType(model, resource.Request);
            SchemataProtoModelConfigurator.ConfigureType(model, resource.Detail);
            SchemataProtoModelConfigurator.ConfigureType(model, resource.Summary);
            SchemataProtoModelConfigurator.ConfigureListResultType(model, resource.Summary!);
        }

        foreach (var (handle, methods) in options.Methods) {
            if (!options.Resources.TryGetValue(handle, out var resource) || !GrpcResourceHelper.IsGrpcEnabled(resource)) {
                continue;
            }

            foreach (var method in methods) {
                var iface = ResourceMethodHandlerHelper.FindHandlerInterface(method.Handler);
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
}
