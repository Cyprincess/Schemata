using Schemata.Mapping.AutoMapper;
using Schemata.Mapping.Foundation;
using Schemata.Mapping.Foundation.Features;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

public static class SchemataMappingBuilderExtensions
{
    public static SchemataMappingBuilder UseAutoMapper(this SchemataMappingBuilder builder) {
        builder.Builder.AddFeature<SchemataMappingFeature<SimpleMapper>>();

        return builder;
    }
}
