using Mapster;
using Schemata.Core;
using Schemata.Mapping.Foundation;
using Schemata.Mapping.Foundation.Features;
using Schemata.Mapping.Mapster;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

public static class SchemataMappingBuilderExtensions
{
    public static SchemataMappingBuilder UseMapster(this SchemataMappingBuilder builder) {
        TypeAdapterConfig.GlobalSettings.Default
                         .IgnoreNullValues(true)
                         .PreserveReference(true);

        builder.Schemata.AddFeature<SchemataMappingFeature<SimpleMapper>>();

        return builder;
    }
}
