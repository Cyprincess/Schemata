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
            if (resource.Endpoints is not null
             && resource.Endpoints.Count != 0
             && resource.Endpoints.All(e => e != GrpcResourceAttribute.Name)) {
                continue;
            }

            SchemataProtoModelConfigurator.ConfigureType(model, resource.Request);
            SchemataProtoModelConfigurator.ConfigureType(model, resource.Detail);
            SchemataProtoModelConfigurator.ConfigureType(model, resource.Summary);
            SchemataProtoModelConfigurator.ConfigureListResultType(model, resource.Summary!);
        }

        return model;
    }
}
