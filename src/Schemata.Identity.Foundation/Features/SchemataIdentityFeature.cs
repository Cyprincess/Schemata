using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Transport.Http.Features;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Identity.Foundation.Features;

/// <summary>
///     Marks the Identity subsystem as installed. The persisted entity types are chosen by
///     <see cref="SchemataIdentityFeature{TUser,TRole,TUserClaim,TRoleClaim,TUserLogin,TUserToken,TUserRole,TUserStore,TRoleStore}" />;
///     this marker only carries the feature ordering contract, so a transport bridge depending on it
///     never forces a closed entity set.
/// </summary>
[DependsOn<SchemataAuthenticationFeature>]
[DependsOn<SchemataTransportHttpFeature>]
public sealed class SchemataIdentityFeature : FeatureBase
{
    /// <summary>Default priority for identity feature startup.</summary>
    public const int DefaultPriority = Orders.Extension + 30_000_000;

    public override int Priority => DefaultPriority;
}

/// <summary>
///     Wires Schemata's Identity-backed API endpoints, controllers, and request advisors closed on
///     the host-selected user, role, and auxiliary identity entities.
/// </summary>
/// <typeparam name="TUser">User entity type.</typeparam>
/// <typeparam name="TRole">Role entity type.</typeparam>
/// <typeparam name="TUserClaim">User claim entity type.</typeparam>
/// <typeparam name="TRoleClaim">Role claim entity type.</typeparam>
/// <typeparam name="TUserLogin">User login entity type.</typeparam>
/// <typeparam name="TUserToken">User token entity type.</typeparam>
/// <typeparam name="TUserRole">User-role link entity type.</typeparam>
/// <typeparam name="TUserStore">User store implementation type.</typeparam>
/// <typeparam name="TRoleStore">Role store implementation type.</typeparam>
[DependsOn<SchemataIdentityFeature>]
public sealed class SchemataIdentityFeature<TUser, TRole, TUserClaim, TRoleClaim, TUserLogin, TUserToken, TUserRole,
    TUserStore, TRoleStore> : FeatureBase
    where TUser : SchemataUser, new()
    where TRole : SchemataRole, new()
    where TUserClaim : SchemataUserClaim, new()
    where TRoleClaim : SchemataRoleClaim, new()
    where TUserLogin : SchemataUserLogin, new()
    where TUserToken : SchemataUserToken, new()
    where TUserRole : SchemataUserRole, new()
    where TUserStore : class, IUserStore<TUser>
    where TRoleStore : class, IRoleStore<TRole>
{
    public override int Priority => SchemataIdentityFeature.DefaultPriority;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        services.AddSchemataApplicationPart<
            SchemataIdentityFeature<TUser, TRole, TUserClaim, TRoleClaim, TUserLogin, TUserToken, TUserRole, TUserStore, TRoleStore>>();
        services.AddSchemataIdentity<TUser, TRole, TUserClaim, TRoleClaim, TUserLogin, TUserToken, TUserRole, TUserStore, TRoleStore>(
            configurators.Pop<IdentityOptions>(),
            configurators.Pop<IdentityBuilder>());
    }
}
