using Schemata.Resource.Foundation;
using Schemata.Resource.Http;
using Schemata.Resource.Http.Features;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

public static class SchemataResourceBuilderExtensions
{
    public static SchemataHttpResourceBuilder MapHttp(this SchemataResourceBuilder builder) {
        builder.Builder.AddFeature<SchemataHttpResourceFeature>();

        return new(builder);
    }
}
