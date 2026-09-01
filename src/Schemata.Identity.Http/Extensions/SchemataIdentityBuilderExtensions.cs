using Schemata.Identity.Foundation;
using Schemata.Identity.Http.Features;
using Schemata.Identity.Skeleton.Entities;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

public static class SchemataIdentityBuilderExtensions
{
    public static SchemataIdentityBuilder<TUser, TRole> MapHttp<TUser, TRole>(this SchemataIdentityBuilder<TUser, TRole> builder)
        where TUser : SchemataUser, new()
        where TRole : SchemataRole {
        builder.AddFeature<SchemataIdentityHttpFeature<TUser, TRole>>();
        return builder;
    }
}
