using System;
using System.Collections.Generic;
using Schemata.Transport.Grpc;
using System.Linq;
using Schemata.Common;
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
    /// <param name="contributors">Domain wire adapters applied before contract registration.</param>
    /// <returns>The configured protobuf-net runtime model.</returns>
    public static RuntimeTypeModel Configure(ResourceRegistry registry, IEnumerable<IGrpcRuntimeModelContributor> contributors) {
        var model = RuntimeTypeModel.Create();

        model.DefaultCompatibilityLevel = CompatibilityLevel.Level300;
        foreach (var contributor in contributors) {
            contributor.Configure(model);
        }

        SchemataProtoModelConfigurator.ConfigureType(model, typeof(ListRequest));
        SchemataProtoModelConfigurator.ConfigureType(model, typeof(GetRequest));
        SchemataProtoModelConfigurator.ConfigureType(model, typeof(DeleteRequest));

        var empty = model.Add(typeof(Google.Protobuf.WellKnownTypes.Empty), false);
        empty.Name = ".google.protobuf.Empty";
        empty.Origin = "google/protobuf/empty.proto";
        foreach (var resource in registry.Resources) {
            if (!GrpcResourceHelper.IsGrpcEnabled(resource)) {
                continue;
            }

            SchemataProtoModelConfigurator.ConfigureType(model, resource.Request);
            SchemataProtoModelConfigurator.ConfigureType(model, resource.Detail);
            SchemataProtoModelConfigurator.ConfigureType(model, resource.Summary);
            SchemataProtoModelConfigurator.ConfigureListResultType(model, resource.Entity, resource.Summary);
            var descriptor = ResourceNameDescriptor.ForType(resource.Entity);
            model[typeof(ListResultBase<,>).MakeGenericType(resource.Entity, resource.Summary)].Name = $"List{descriptor.Plural}Response";
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
                model[descriptor.Request].Name = MessageName(descriptor.Request);
                model[descriptor.Response].Name = MessageName(descriptor.Response);
            }
        }

        return model;
    }

    private static string MessageName(Type type) {
        var arity = type.Name.IndexOf('`');
        return arity < 0 ? type.Name : $"{type.Name[..arity]}Of{string.Join("And", type.GetGenericArguments().Select(MessageName))}";
    }
}
