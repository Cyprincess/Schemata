using Schemata.Core;
using Schemata.Quota.Foundation;
using Schemata.Quota.Http.Features;

namespace Microsoft.AspNetCore.Builder;

public static class SchemataQuotaBuilderExtensions
{
    public static SchemataQuotaBuilder MapHttp(this SchemataQuotaBuilder builder) {
        builder.Options.AddFeature<SchemataQuotaHttpFeature>();
        return builder;
    }
}
