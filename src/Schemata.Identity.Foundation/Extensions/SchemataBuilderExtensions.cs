using System;
using Microsoft.AspNetCore.Identity;
using Schemata.Core;
using Schemata.Identity.Foundation.Features;
using Schemata.Identity.Skeleton.Entities;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

public static class SchemataBuilderExtensions
{
    public static SchemataBuilder UseIdentity(
        this SchemataBuilder     builder,
        Action<IdentityOptions>? configure = null,
        Action<IdentityBuilder>? build     = null) {
        return UseIdentity<SchemataUser, SchemataRole>(builder, configure, build);
    }

    public static SchemataBuilder UseIdentity<TUser, TRole>(
        this SchemataBuilder     builder,
        Action<IdentityOptions>? configure = null,
        Action<IdentityBuilder>? build     = null)
        where TUser : SchemataUser
        where TRole : SchemataRole {
        return UseIdentity<TUser, TRole, IUserStore<TUser>, IRoleStore<TRole>>(builder, configure, build);
    }

    public static SchemataBuilder UseIdentity<TUser, TRole, TUserStore, TRoleStore>(
        this SchemataBuilder     builder,
        Action<IdentityOptions>? configure = null,
        Action<IdentityBuilder>? build     = null)
        where TUser : SchemataUser
        where TRole : SchemataRole
        where TUserStore : class, IUserStore<TUser>
        where TRoleStore : class, IRoleStore<TRole> {
        configure ??= _ => { };
        builder.Configure(configure);

        build ??= _ => { };
        builder.Configure(build);

        builder.AddFeature<SchemataIdentityFeature<TUser, TRole, TUserStore, TRoleStore>>();

        return builder;
    }
}
