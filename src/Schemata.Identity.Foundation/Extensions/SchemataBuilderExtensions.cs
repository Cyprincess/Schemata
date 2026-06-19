using System;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Identity;
using Schemata.Core;
using Schemata.Identity.Foundation;
using Schemata.Identity.Foundation.Features;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Stores;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>Configures the Schemata identity feature.</summary>
public static class SchemataBuilderExtensions
{
    /// <summary>Adds identity services using the default user, role, and store types.</summary>
    /// <param name="builder">Schemata builder receiving the feature.</param>
    /// <param name="identify">Schemata identity options callback.</param>
    /// <param name="configure">ASP.NET Core Identity options callback.</param>
    /// <param name="build">Identity builder callback.</param>
    /// <param name="bearer">Bearer token options callback.</param>
    /// <returns>The Schemata builder.</returns>
    public static SchemataBuilder UseIdentity(
        this SchemataBuilder             builder,
        Action<SchemataIdentityOptions>? identify  = null,
        Action<IdentityOptions>?         configure = null,
        Action<IdentityBuilder>?         build     = null,
        Action<BearerTokenOptions>?      bearer    = null
    ) {
        return builder.UseIdentity<SchemataUser, SchemataRole>(identify, configure, build, bearer);
    }

    /// <summary>Adds identity services using custom user and role types with default stores.</summary>
    /// <typeparam name="TUser">User entity type.</typeparam>
    /// <typeparam name="TRole">Role entity type.</typeparam>
    /// <param name="builder">Schemata builder receiving the feature.</param>
    /// <param name="identify">Schemata identity options callback.</param>
    /// <param name="configure">ASP.NET Core Identity options callback.</param>
    /// <param name="build">Identity builder callback.</param>
    /// <param name="bearer">Bearer token options callback.</param>
    /// <returns>The Schemata builder.</returns>
    public static SchemataBuilder UseIdentity<TUser, TRole>(
        this SchemataBuilder             builder,
        Action<SchemataIdentityOptions>? identify  = null,
        Action<IdentityOptions>?         configure = null,
        Action<IdentityBuilder>?         build     = null,
        Action<BearerTokenOptions>?      bearer    = null
    )
        where TUser : SchemataUser, new()
        where TRole : SchemataRole {
        return builder.UseIdentity<TUser, TRole, SchemataUserStore<TUser>, SchemataRoleStore<TRole>>(identify, configure, build, bearer);
    }

    /// <summary>Adds identity services using custom user, role, and store types.</summary>
    /// <typeparam name="TUser">User entity type.</typeparam>
    /// <typeparam name="TRole">Role entity type.</typeparam>
    /// <typeparam name="TUserStore">User store implementation type.</typeparam>
    /// <typeparam name="TRoleStore">Role store implementation type.</typeparam>
    /// <param name="builder">Schemata builder receiving the feature.</param>
    /// <param name="identify">Schemata identity options callback.</param>
    /// <param name="configure">ASP.NET Core Identity options callback.</param>
    /// <param name="build">Identity builder callback.</param>
    /// <param name="bearer">Bearer token options callback.</param>
    /// <returns>The Schemata builder.</returns>
    public static SchemataBuilder UseIdentity<TUser, TRole, TUserStore, TRoleStore>(
        this SchemataBuilder             builder,
        Action<SchemataIdentityOptions>? identify  = null,
        Action<IdentityOptions>?         configure = null,
        Action<IdentityBuilder>?         build     = null,
        Action<BearerTokenOptions>?      bearer    = null
    )
        where TUser : SchemataUser, new()
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
