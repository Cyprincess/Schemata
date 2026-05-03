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
            // Remove CanonicalName from serialization — it is a fully-qualified
            // resource path (AIP-122) reconstructed from the hierarchical JSON
            // structure, not a stored property.
            if (typeof(ICanonicalName).IsAssignableFrom(info.Type)) {
                var property = info.Properties.FirstOrDefault(p => p.AttributeProvider is MemberInfo {
                    Name: nameof(ICanonicalName.CanonicalName),
                });

                if (property is not null) {
                    info.Properties.Remove(property);
                }
            }

            // Rename Entities to the pluralized entity name per AIP-132,
            // which requires the repeated field in a List response to use
            // the resource's plural name (e.g. "books" instead of "entities").
            if (info.Type is { IsGenericType: true } && info.Type.GetGenericTypeDefinition() == typeof(ListResult<>)) {
                var property = info.Properties.FirstOrDefault(p => p.AttributeProvider is MemberInfo {
                    Name: nameof(ListResult<>.Entities),
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

            // Rename EntityTag to "etag" per AIP-154 convention,
            // matching the query parameter name used for concurrency control.
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
