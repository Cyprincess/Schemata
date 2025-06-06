using System;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Identity;
using Schemata.Abstractions.Options;
using Schemata.Core;
using Schemata.Identity.Foundation.Features;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Stores;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

public static class SchemataBuilderExtensions
{
    public static SchemataBuilder UseIdentity(
        this SchemataBuilder             builder,
        Action<SchemataIdentityOptions>? identify  = null,
        Action<IdentityOptions>?         configure = null,
        Action<IdentityBuilder>?         build     = null,
        Action<BearerTokenOptions>?      bearer    = null) {
        return UseIdentity<SchemataUser, SchemataRole>(builder, identify, configure, build, bearer);
    }

    public static SchemataBuilder UseIdentity<TUser, TRole>(
        this SchemataBuilder             builder,
        Action<SchemataIdentityOptions>? identify  = null,
        Action<IdentityOptions>?         configure = null,
        Action<IdentityBuilder>?         build     = null,
        Action<BearerTokenOptions>?      bearer    = null) where TUser : SchemataUser
                                                           where TRole : SchemataRole {
        return UseIdentity<TUser, TRole, SchemataUserStore<TUser>, SchemataRoleStore<TRole>>(builder, identify, configure, build, bearer);
    }

    public static SchemataBuilder UseIdentity<TUser, TRole, TUserStore, TRoleStore>(
        this SchemataBuilder             builder,
        Action<SchemataIdentityOptions>? identify  = null,
        Action<IdentityOptions>?         configure = null,
        Action<IdentityBuilder>?         build     = null,
        Action<BearerTokenOptions>?      bearer    = null) where TUser : SchemataUser
                                                           where TRole : SchemataRole
                                                           where TUserStore : class, IUserStore<TUser>
                                                           where TRoleStore : class, IRoleStore<TRole> {
        identify ??= _ => { };
        builder.Configure(identify);

        configure ??= _ => { };
        builder.Configure(configure);

        build ??= _ => { };
        builder.Configure(build);

        bearer ??= _ => { };
        builder.Configure(bearer);

        builder.AddFeature<SchemataIdentityFeature<TUser, TRole, TUserStore, TRoleStore>>();

        return builder;
    }
}
