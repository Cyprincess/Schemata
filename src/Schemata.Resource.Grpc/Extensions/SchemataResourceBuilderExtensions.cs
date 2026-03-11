using Schemata.Resource.Foundation;
using Schemata.Resource.Grpc;
using Schemata.Resource.Grpc.Features;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

public static class SchemataResourceBuilderExtensions
{
    public static SchemataGrpcResourceBuilder MapGrpc(this SchemataResourceBuilder builder) {
        builder.AddFeature<SchemataGrpcResourceFeature>();

        return new(builder);
    }
}
