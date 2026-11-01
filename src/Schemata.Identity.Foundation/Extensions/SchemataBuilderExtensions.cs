using System;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Identity;
using Schemata.Entity.Repository;
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
    /// <summary>Adds identity services using the default user, role, and auxiliary entity types.</summary>
    /// <param name="builder">Schemata builder receiving the feature.</param>
    /// <param name="identify">Schemata identity options callback.</param>
    /// <param name="configure">ASP.NET Core Identity options callback.</param>
    /// <param name="build">Identity builder callback.</param>
    /// <param name="bearer">Bearer token options callback.</param>
    /// <returns>The Identity resource-management builder.</returns>
    public static SchemataIdentityBuilder<SchemataUser, SchemataRole> UseIdentity(
        this SchemataBuilder             builder,
        Action<SchemataIdentityOptions>? identify  = null,
        Action<IdentityOptions>?         configure = null,
        Action<IdentityBuilder>?         build     = null,
        Action<BearerTokenOptions>?      bearer    = null
    ) {
        return builder.UseIdentity<
            SchemataUser, SchemataRole, SchemataUserClaim, SchemataRoleClaim, SchemataUserLogin, SchemataUserToken,
            SchemataUserRole>(identify, configure, build, bearer);
    }

    /// <summary>Adds identity services using custom user and role types with the default auxiliary entities.</summary>
    /// <typeparam name="TUser">User entity type.</typeparam>
    /// <typeparam name="TRole">Role entity type.</typeparam>
    /// <param name="builder">Schemata builder receiving the feature.</param>
    /// <param name="identify">Schemata identity options callback.</param>
    /// <param name="configure">ASP.NET Core Identity options callback.</param>
    /// <param name="build">Identity builder callback.</param>
    /// <param name="bearer">Bearer token options callback.</param>
    /// <returns>The Identity resource-management builder.</returns>
    public static SchemataIdentityBuilder<TUser, TRole> UseIdentity<TUser, TRole>(
        this SchemataBuilder             builder,
        Action<SchemataIdentityOptions>? identify  = null,
        Action<IdentityOptions>?         configure = null,
        Action<IdentityBuilder>?         build     = null,
        Action<BearerTokenOptions>?      bearer    = null
    )
        where TUser : SchemataUser, new()
        where TRole : SchemataRole, new() {
        return builder.UseIdentity<
            TUser, TRole, SchemataUserClaim, SchemataRoleClaim, SchemataUserLogin, SchemataUserToken, SchemataUserRole>(
            identify, configure, build, bearer);
    }

    /// <summary>
    ///     Adds identity services using host-selected user, role, and auxiliary entity types with the
    ///     default stores.
    /// </summary>
    /// <typeparam name="TUser">User entity type.</typeparam>
    /// <typeparam name="TRole">Role entity type.</typeparam>
    /// <typeparam name="TUserClaim">User claim entity type.</typeparam>
    /// <typeparam name="TRoleClaim">Role claim entity type.</typeparam>
    /// <typeparam name="TUserLogin">User login entity type.</typeparam>
    /// <typeparam name="TUserToken">User token entity type.</typeparam>
    /// <typeparam name="TUserRole">User-role link entity type.</typeparam>
    /// <param name="builder">Schemata builder receiving the feature.</param>
    /// <param name="identify">Schemata identity options callback.</param>
    /// <param name="configure">ASP.NET Core Identity options callback.</param>
    /// <param name="build">Identity builder callback.</param>
    /// <param name="bearer">Bearer token options callback.</param>
    /// <returns>The Identity resource-management builder.</returns>
    public static SchemataIdentityBuilder<TUser, TRole> UseIdentity<TUser, TRole, TUserClaim, TRoleClaim, TUserLogin,
        TUserToken, TUserRole>(
        this SchemataBuilder             builder,
        Action<SchemataIdentityOptions>? identify  = null,
        Action<IdentityOptions>?         configure = null,
        Action<IdentityBuilder>?         build     = null,
        Action<BearerTokenOptions>?      bearer    = null
    )
        where TUser : SchemataUser, new()
        where TRole : SchemataRole, new()
        where TUserClaim : SchemataUserClaim, new()
        where TRoleClaim : SchemataRoleClaim, new()
        where TUserLogin : SchemataUserLogin, new()
        where TUserToken : SchemataUserToken, new()
        where TUserRole : SchemataUserRole, new() {
        return builder.UseIdentityCore<
            TUser, TRole, TUserClaim, TRoleClaim, TUserLogin, TUserToken, TUserRole,
            SchemataUserStore<TUser, TRole, TUserClaim, TUserRole, TUserLogin, TUserToken, TRoleClaim>,
            SchemataRoleStore<TRole, TRoleClaim, TUserRole>>(identify, configure, build, bearer);
    }

    /// <summary>
    ///     Adds identity services using custom user and role stores. The auxiliary entity types are
    ///     the built-in Schemata entities, and the delete cascade is closed on them.
    /// </summary>
    /// <remarks>
    ///     A store that persists its own relation entities registers
    ///     <see cref="IResourceMutation{TEntity}" /> for <typeparamref name="TUser" /> and
    ///     <typeparamref name="TRole" /> explicitly; the explicit registration is preserved and owns
    ///     the delete cascade.
    /// </remarks>
    /// <typeparam name="TUser">User entity type.</typeparam>
    /// <typeparam name="TRole">Role entity type.</typeparam>
    /// <typeparam name="TUserStore">User store implementation type.</typeparam>
    /// <typeparam name="TRoleStore">Role store implementation type.</typeparam>
    /// <param name="builder">Schemata builder receiving the feature.</param>
    /// <param name="identify">Schemata identity options callback.</param>
    /// <param name="configure">ASP.NET Core Identity options callback.</param>
    /// <param name="build">Identity builder callback.</param>
    /// <param name="bearer">Bearer token options callback.</param>
    /// <returns>The Identity resource-management builder.</returns>
    public static SchemataIdentityBuilder<TUser, TRole> UseIdentity<TUser, TRole, TUserStore, TRoleStore>(
        this SchemataBuilder             builder,
        Action<SchemataIdentityOptions>? identify  = null,
        Action<IdentityOptions>?         configure = null,
        Action<IdentityBuilder>?         build     = null,
        Action<BearerTokenOptions>?      bearer    = null
    )
        where TUser : SchemataUser, new()
        where TRole : SchemataRole, new()
        where TUserStore : class, IUserStore<TUser>
        where TRoleStore : class, IRoleStore<TRole> {
        return builder.UseIdentityCore<
            TUser, TRole, SchemataUserClaim, SchemataRoleClaim, SchemataUserLogin, SchemataUserToken, SchemataUserRole,
            TUserStore, TRoleStore>(identify, configure, build, bearer);
    }

    private static SchemataIdentityBuilder<TUser, TRole> UseIdentityCore<TUser, TRole, TUserClaim, TRoleClaim, TUserLogin,
        TUserToken, TUserRole, TUserStore, TRoleStore>(
        this SchemataBuilder             builder,
        Action<SchemataIdentityOptions>? identify,
        Action<IdentityOptions>?         configure,
        Action<IdentityBuilder>?         build,
        Action<BearerTokenOptions>?      bearer
    )
        where TUser : SchemataUser, new()
        where TRole : SchemataRole, new()
        where TUserClaim : SchemataUserClaim, new()
        where TRoleClaim : SchemataRoleClaim, new()
        where TUserLogin : SchemataUserLogin, new()
        where TUserToken : SchemataUserToken, new()
        where TUserRole : SchemataUserRole, new()
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

        builder.AddFeature<SchemataIdentityFeature<TUser, TRole, TUserClaim, TRoleClaim, TUserLogin, TUserToken, TUserRole,
            TUserStore, TRoleStore>>();

        return new(builder.Options, builder.Services);
    }
}
