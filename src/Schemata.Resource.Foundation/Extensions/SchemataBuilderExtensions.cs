using Schemata.Core;
using Schemata.Resource.Foundation;
using Schemata.Resource.Foundation.Features;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

public static class SchemataBuilderExtensions
{
    public static SchemataResourceBuilder UseResource(this SchemataBuilder builder) {
        builder.AddFeature<SchemataResourceFeature>();

        return new(builder);
    }
}
