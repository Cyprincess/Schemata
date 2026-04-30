using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Schemata.Abstractions.Json;
using Schemata.Common;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Core.Json;

/// <summary>
///     JSON type info resolver that scans the application domain for types
///     decorated with <see cref="PolymorphicAttribute" /> and configures
///     <see cref="JsonPolymorphismOptions" /> on base types with discriminator
///     property <c>"@type"</c>. Unknown discriminators fail serialization.
/// </summary>
public class PolymorphicTypeResolver : DefaultJsonTypeInfoResolver
{
    private readonly Dictionary<RuntimeTypeHandle, List<JsonDerivedType>?> _types = [];

    private PolymorphicTypeResolver() {
        var types = AppDomainTypeCache.Types.Values.Where(t => t.HasCustomAttribute<PolymorphicAttribute>(false));
        foreach (var type in types) {
            var attribute = type.GetCustomAttribute<PolymorphicAttribute>();
            var handle    = attribute!.BaseType.TypeHandle;
            if (!_types.TryGetValue(handle, out var value)) {
                value          = [];
                _types[handle] = value;
            }

            value!.Add(attribute.Name is not null ? new(type, attribute.Name) : new(type));
        }
    }

    /// <summary>
    ///     Singleton instance registered by
    ///     <see cref="Features.SchemataJsonSerializerFeature" />.
    /// </summary>
    public static PolymorphicTypeResolver Instance { get; } = new();

    /// <inheritdoc />
    public override JsonTypeInfo GetTypeInfo(Type type, JsonSerializerOptions options) {
        var info = base.GetTypeInfo(type, options);

        if (!_types.TryGetValue(info.Type.TypeHandle, out var children)) {
            return info;
        }

        info.PolymorphismOptions = new() {
            TypeDiscriminatorPropertyName        = Parameters.Type,
            IgnoreUnrecognizedTypeDiscriminators = true,
            UnknownDerivedTypeHandling           = JsonUnknownDerivedTypeHandling.FailSerialization,
        };
        foreach (var child in children!) {
            info.PolymorphismOptions.DerivedTypes.Add(child);
        }

        return info;
    }
}
