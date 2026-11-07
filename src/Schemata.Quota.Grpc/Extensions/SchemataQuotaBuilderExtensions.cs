using Schemata.Core;
using Schemata.Quota.Foundation;
using Schemata.Quota.Grpc.Features;

namespace Microsoft.AspNetCore.Builder;

public static class SchemataQuotaBuilderExtensions
{
    public static SchemataQuotaBuilder MapGrpc(this SchemataQuotaBuilder builder) {
        builder.Options.AddFeature<SchemataQuotaGrpcFeature>();
        return builder;
    }
}
