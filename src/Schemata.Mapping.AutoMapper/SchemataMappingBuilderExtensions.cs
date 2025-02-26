using AutoMapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Schemata.Core;
using Schemata.Mapping.AutoMapper;
using Schemata.Mapping.Foundation;
using Schemata.Mapping.Foundation.Features;
using Schemata.Mapping.Skeleton;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

public static class SchemataMappingBuilderExtensions
{
    public static SchemataMappingBuilder UseAutoMapper(this SchemataMappingBuilder builder) {
        builder.Services.TryAddSingleton(sp => {
            var options = sp.GetRequiredService<IOptions<SchemataMappingOptions>>();

            return new MapperConfiguration(mapper => {
                AutoMapperConfigurator.Configure(mapper, options.Value);
            });
        });

        builder.Schemata.AddFeature<SchemataMappingFeature<SimpleMapper>>();

        return builder;
    }
}
