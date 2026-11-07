using Schemata.Identity.Foundation;
using Schemata.Identity.Scim.Features;
using Schemata.Identity.Skeleton.Entities;

namespace Microsoft.AspNetCore.Builder;

/// <summary>Configures SCIM for the host-selected Identity entity types.</summary>
public static class SchemataIdentityBuilderExtensions
{
    public static SchemataIdentityBuilder<TUser, TRole> UseScim<TUser, TRole>(
        this SchemataIdentityBuilder<TUser, TRole> builder
    )
        where TUser : SchemataUser, new()
        where TRole : SchemataRole, new() {
        builder.AddFeature<SchemataIdentityScimFeature<TUser, TRole>>();
        return builder;
    }
}
