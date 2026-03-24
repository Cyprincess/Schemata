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

/// <summary>
///     Extension methods for configuring Schemata identity features on <see cref="SchemataBuilder"/>.
/// </summary>
public static class SchemataBuilderExtensions
{
    /// <summary>
    ///     Adds the Schemata identity feature using default entity and store types.
    /// </summary>
    /// <param name="builder">The Schemata builder.</param>
    /// <param name="identify">Optional configurator for <see cref="SchemataIdentityOptions"/>.</param>
    /// <param name="configure">Optional configurator for ASP.NET Core <see cref="IdentityOptions"/>.</param>
    /// <param name="build">Optional callback to further configure the <see cref="IdentityBuilder"/>.</param>
    /// <param name="bearer">Optional configurator for <see cref="BearerTokenOptions"/>.</param>
    /// <returns>The builder for chaining.</returns>
    public static SchemataBuilder UseIdentity(
        this SchemataBuilder             builder,
        Action<SchemataIdentityOptions>? identify  = null,
        Action<IdentityOptions>?         configure = null,
        Action<IdentityBuilder>?         build     = null,
        Action<BearerTokenOptions>?      bearer    = null
    ) {
        return builder.UseIdentity<SchemataUser, SchemataRole>(identify, configure, build, bearer);
    }

    /// <summary>
    ///     Adds the Schemata identity feature with custom user and role types.
    /// </summary>
    /// <typeparam name="TUser">The user entity type.</typeparam>
    /// <typeparam name="TRole">The role entity type.</typeparam>
    /// <param name="builder">The Schemata builder.</param>
    /// <param name="identify">Optional configurator for <see cref="SchemataIdentityOptions"/>.</param>
    /// <param name="configure">Optional configurator for ASP.NET Core <see cref="IdentityOptions"/>.</param>
    /// <param name="build">Optional callback to further configure the <see cref="IdentityBuilder"/>.</param>
    /// <param name="bearer">Optional configurator for <see cref="BearerTokenOptions"/>.</param>
    /// <returns>The builder for chaining.</returns>
    public static SchemataBuilder UseIdentity<TUser, TRole>(
        this SchemataBuilder             builder,
        Action<SchemataIdentityOptions>? identify  = null,
        Action<IdentityOptions>?         configure = null,
        Action<IdentityBuilder>?         build     = null,
        Action<BearerTokenOptions>?      bearer    = null
    )
        where TUser : SchemataUser
        where TRole : SchemataRole {
        return builder.UseIdentity<TUser, TRole, SchemataUserStore<TUser>, SchemataRoleStore<TRole>>(identify, configure, build, bearer);
    }

    /// <summary>
    ///     Adds the Schemata identity feature with fully custom user, role, and store types.
    /// </summary>
    /// <typeparam name="TUser">The user entity type.</typeparam>
    /// <typeparam name="TRole">The role entity type.</typeparam>
    /// <typeparam name="TUserStore">The user store implementation type.</typeparam>
    /// <typeparam name="TRoleStore">The role store implementation type.</typeparam>
    /// <param name="builder">The Schemata builder.</param>
    /// <param name="identify">Optional configurator for <see cref="SchemataIdentityOptions"/>.</param>
    /// <param name="configure">Optional configurator for ASP.NET Core <see cref="IdentityOptions"/>.</param>
    /// <param name="build">Optional callback to further configure the <see cref="IdentityBuilder"/>.</param>
    /// <param name="bearer">Optional configurator for <see cref="BearerTokenOptions"/>.</param>
    /// <returns>The builder for chaining.</returns>
    public static SchemataBuilder UseIdentity<TUser, TRole, TUserStore, TRoleStore>(
        this SchemataBuilder             builder,
        Action<SchemataIdentityOptions>? identify  = null,
        Action<IdentityOptions>?         configure = null,
        Action<IdentityBuilder>?         build     = null,
        Action<BearerTokenOptions>?      bearer    = null
    )
        where TUser : SchemataUser
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
