using System;
using System.Collections.Generic;
using System.Linq;
using Humanizer;
using ProtoBuf;
using ProtoBuf.Meta;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Resource.Foundation;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Resource.Grpc;

internal static class RuntimeTypeModelConfigurator
{
    private static readonly HashSet<RuntimeTypeHandle> Configured = [];

    public static RuntimeTypeModel Configure(SchemataResourceOptions options) {
        var model = RuntimeTypeModel.Create();

        model.DefaultCompatibilityLevel = CompatibilityLevel.Level300;

        ConfigureType(model, typeof(ListRequest));
        ConfigureType(model, typeof(GetRequest));
        ConfigureType(model, typeof(DeleteRequest));

        foreach (var (_, resource) in options.Resources) {
            if (resource.Endpoints is not null
             && resource.Endpoints.Count != 0
             && resource.Endpoints.All(e => e != GrpcResourceAttribute.Name)) {
                continue;
            }

            ConfigureType(model, resource.Request);
            ConfigureType(model, resource.Detail);
            ConfigureType(model, resource.Summary);
            ConfigureListResponseType(model, resource.Summary!, resource.Entity);
        }

        return model;
    }

    private static void ConfigureType(RuntimeTypeModel model, Type? type) {
        if (type is null || !Configured.Add(type.TypeHandle)) {
            return;
        }

        if (model.CanSerialize(type)) {
            return;
        }

        if (type is { IsGenericType: true, IsConstructedGenericType: false }) {
            return;
        }

        var meta = model.Add(type, true);

        var properties = AppDomainTypeCache.GetWritableProperties(type).ToList();

        var number = 1;
        foreach (var property in properties) {
            var name = property.Name;

            switch (name) {
                case nameof(ICanonicalName.Name) when typeof(ICanonicalName).IsAssignableFrom(type):
                    continue;

                case nameof(ICanonicalName.CanonicalName) when typeof(ICanonicalName).IsAssignableFrom(type):
                    name = nameof(ICanonicalName.Name);
                    break;

                case nameof(IFreshness.EntityTag) when typeof(IFreshness).IsAssignableFrom(type):
                    name = Parameters.EntityTag;
                    break;
            }

            if (meta.GetFields().Any(f => f.Name == property.Name)) {
                continue;
            }

            var field = meta.AddField(number++, property.Name);
            field.Name = name.Underscore();

            var underlying = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

            if (underlying.IsClass && underlying != typeof(string) && !underlying.IsArray) {
                ConfigureType(model, underlying);
            }

            if (!underlying.IsGenericType) {
                continue;
            }

            foreach (var argument in underlying.GetGenericArguments()) {
                if (argument.IsClass && argument != typeof(string)) {
                    ConfigureType(model, argument);
                }
            }
        }
    }

    public static void ConfigureListResponseType(RuntimeTypeModel model, Type summary, Type entity) {
        var response = typeof(ListResult<>).MakeGenericType(summary);
        ConfigureType(model, response);

        var meta  = model[response];
        var field = meta.GetFields().FirstOrDefault(f => f.Name == "entities");
        field?.Name = ResourceNameDescriptor.ForType(entity).Plural.Underscore();
    }
}
