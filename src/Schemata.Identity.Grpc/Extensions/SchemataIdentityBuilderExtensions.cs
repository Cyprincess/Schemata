using Schemata.Identity.Foundation;
using Schemata.Identity.Grpc.Features;
using Schemata.Identity.Skeleton.Entities;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

public static class SchemataIdentityBuilderExtensions
{
    public static SchemataIdentityBuilder<TUser, TRole> MapGrpc<TUser, TRole>(this SchemataIdentityBuilder<TUser, TRole> builder)
        where TUser : SchemataUser, new()
        where TRole : SchemataRole {
        builder.AddFeature<SchemataIdentityGrpcFeature<TUser, TRole>>();
        return builder;
    }
}
