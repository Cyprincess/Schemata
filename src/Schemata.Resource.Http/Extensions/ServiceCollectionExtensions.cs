using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Humanizer;
using Microsoft.Extensions.Options;
using Schemata.Abstractions;
using Schemata.Abstractions.Resource;
using Schemata.Core.Json;
using Schemata.Resource.Http;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddResourceJsonSerializerOptions(this IServiceCollection services)
    {
        services.AddSingleton<ResourceJsonSerializerOptions>(sp => {
            var options = sp.GetRequiredService<IOptions<JsonSerializerOptions>>();

            var inner = new JsonSerializerOptions(options.Value) {
                TypeInfoResolver = new PolymorphicTypeResolver(),
            };

            var entities = inner.PropertyNamingPolicy is not null
                ? inner.PropertyNamingPolicy.ConvertName(nameof(ListResponse<long>.Entities))
                : nameof(ListResponse<long>.Entities);

            var etag = inner.PropertyNamingPolicy is not null
                ? inner.PropertyNamingPolicy.ConvertName(nameof(IFreshness.EntityTag))
                : nameof(IFreshness.EntityTag);

            inner.TypeInfoResolver = inner.TypeInfoResolver.WithAddedModifier(info => {
                if (info.Type.IsGenericType && info.Type.GetGenericTypeDefinition() == typeof(ListResponse<>)) {
                    var property = info.Properties.FirstOrDefault(p => p.Name == entities);
                    if (property is not null) {
                        var name = info.Type.GetGenericArguments().FirstOrDefault()?.Name.Pluralize() ?? entities;
                        property.Name = inner.PropertyNamingPolicy is not null
                            ? inner.PropertyNamingPolicy.ConvertName(name)
                            : name;
                    }
                }

                if (info.Type.GetInterfaces().Any(t => t == typeof(IFreshness))) {
                    var property = info.Properties.FirstOrDefault(p => p.Name == etag);
                    if (property is not null) {
                        property.Name = SchemataConstants.Parameters.EntityTag;
                    }
                }
            });

            return new(inner);
        });

        return services;
    }
}
