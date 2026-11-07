using Schemata.Core;
using Schemata.Quota.Foundation;
using Schemata.Quota.Foundation.Features;

namespace Microsoft.AspNetCore.Builder;

public static class SchemataBuilderExtensions
{
    public static SchemataQuotaBuilder UseQuotaAccounting(this SchemataBuilder builder) {
        builder.AddFeature<SchemataQuotaAccountingFeature>();
        return new(builder.Options, builder.Services);
    }
}
