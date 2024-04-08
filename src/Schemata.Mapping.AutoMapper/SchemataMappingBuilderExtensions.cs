using Schemata.Mapping.AutoMapper;
using Schemata.Mapping.Foundation;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

public static class SchemataMappingBuilderExtensions
{
    public static SchemataMappingBuilder UseAutoMapper(this SchemataMappingBuilder builder) {
        builder.Builder.AddFeature<SchemataMappingFeature>();

        return builder;
    }
}
