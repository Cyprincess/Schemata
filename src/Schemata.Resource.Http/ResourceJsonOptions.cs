using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Resource.Foundation;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Resource.Http;

internal static class ResourceJsonOptions
{
    private static JsonSerializerOptions? _instance;

    /// <summary>
    ///     Gets or creates a singleton <see cref="JsonSerializerOptions" /> configured with
    ///     resource-specific JSON naming conventions. The instance is built once from
    ///     <paramref name="source" /> and <paramref name="options" />.
    /// </summary>
    /// <param name="source">The base JSON serializer options.</param>
    /// <param name="options">The resource configuration containing entity type mappings.</param>
    /// <returns>A resource-aware <see cref="JsonSerializerOptions" /> instance.</returns>
    public static JsonSerializerOptions GetOrCreate(JsonSerializerOptions source, SchemataResourceOptions options) {
        return _instance ??= Configure(source, options);
    }

    private static JsonSerializerOptions Configure(JsonSerializerOptions source, SchemataResourceOptions options) {
        var json = new JsonSerializerOptions(source);

        var entityDescriptors = new Dictionary<Type, ResourceNameDescriptor>();
        foreach (var (_, resource) in options.Resources) {
            if (resource.Summary is not null) {
                entityDescriptors[resource.Summary] = ResourceNameDescriptor.ForType(resource.Entity);
            }
        }

        json.TypeInfoResolver = json.TypeInfoResolver?.WithAddedModifier(info => {
            if (typeof(ICanonicalName).IsAssignableFrom(info.Type)) {
                // Remove ICanonicalName.Name from serialization — it is the full resource name pattern and
                // not a proto field that should be serialized independently.
                var np = info.Properties.FirstOrDefault(p => p.AttributeProvider is MemberInfo {
                    Name: nameof(ICanonicalName.Name),
                });
                if (np is not null) {
                    info.Properties.Remove(np);
                }

                // Map CanonicalName to "name" for AIP-122 wire compatibility.
                var cp = info.Properties.FirstOrDefault(p => p.AttributeProvider is MemberInfo {
                    Name: nameof(ICanonicalName.CanonicalName),
                });
                cp?.Name = Parameters.Name;
            }

            // Rename "entities" to the pluralized resource name per AIP-132.
            if (info.Type is { IsGenericType: true } && info.Type.GetGenericTypeDefinition() == typeof(ListResultBase<>)) {
                var property = info.Properties.FirstOrDefault(p => p.AttributeProvider is MemberInfo {
                    Name: nameof(ListResultBase<>.Entities),
                });

                if (property is not null) {
                    var summaryType = info.Type.GetGenericArguments()[0];
                    var descriptor = entityDescriptors.TryGetValue(summaryType, out var d)
                        ? d
                        : ResourceNameDescriptor.ForType(summaryType);
                    var name = descriptor.Plural;
                    property.Name = json.PropertyNamingPolicy is not null
                        ? json.PropertyNamingPolicy.ConvertName(name)
                        : name;
                }
            }

            // Map EntityTag to "etag" per AIP-154 concurrency token naming.
            if (typeof(IFreshness).IsAssignableFrom(info.Type)) {
                var property = info.Properties.FirstOrDefault(p => p.AttributeProvider is MemberInfo {
                    Name: nameof(IFreshness.EntityTag),
                });

                property?.Name = Parameters.EntityTag;
            }
        });

        return json;
    }
}
