using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Schemata.Abstractions.Json;
using Schemata.Common;

namespace Schemata.Core.Json;

/// <summary>
///     JSON type info resolver that scans the application domain for types
///     decorated with <see cref="PolymorphicAttribute" /> and configures
///     <see cref="JsonPolymorphismOptions" /> on base types with discriminator
///     property <c>"@type"</c>. Unknown discriminators fail serialization.
/// </summary>
/// <remarks>
///     The scan runs through <see cref="AppDomainTypeCache.GetAllTypes" /> when the singleton is
///     created, so assemblies loaded after the cache was first touched are included. Types loaded
///     only after this resolver is created are not observed; they must be present when the serializer
///     options are configured.
/// </remarks>
public class PolymorphicTypeResolver : DefaultJsonTypeInfoResolver
{
    private const string TypeDiscriminator = "@type";

    private readonly Dictionary<RuntimeTypeHandle, List<JsonDerivedType>> _types = [];

    private PolymorphicTypeResolver() {
        foreach (var type in AppDomainTypeCache.GetAllTypes()) {
            if (type.GetCustomAttribute<PolymorphicAttribute>() is not { } attribute) {
                continue;
            }

            var handle = attribute.BaseType.TypeHandle;
            if (!_types.TryGetValue(handle, out var types)) {
                types          = [];
                _types[handle] = types;
            }

            types.Add(attribute.Name is not null ? new(type, attribute.Name) : new(type));
        }
    }

    /// <summary>
    ///     Singleton instance registered by
    ///     <see cref="Features.SchemataJsonSerializerFeature" />.
    /// </summary>
    public static PolymorphicTypeResolver Instance { get; } = new();

    public override JsonTypeInfo GetTypeInfo(Type type, JsonSerializerOptions options) {
        var info = base.GetTypeInfo(type, options);

        if (!_types.TryGetValue(info.Type.TypeHandle, out var children)) {
            return info;
        }

        info.PolymorphismOptions = new() {
            TypeDiscriminatorPropertyName        = TypeDiscriminator,
            IgnoreUnrecognizedTypeDiscriminators = true,
            UnknownDerivedTypeHandling           = JsonUnknownDerivedTypeHandling.FailSerialization,
        };
        foreach (var child in children) {
            info.PolymorphismOptions.DerivedTypes.Add(child);
        }

        return info;
    }
}
