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
///     Wires Schemata's Identity-backed API endpoints, controllers, and request advisors.
/// </summary>
/// <typeparam name="TUser">User entity type.</typeparam>
/// <typeparam name="TRole">Role entity type.</typeparam>
/// <typeparam name="TUserStore">User store implementation type.</typeparam>
/// <typeparam name="TRoleStore">Role store implementation type.</typeparam>
[DependsOn<SchemataAuthenticationFeature>]
[DependsOn<SchemataTransportHttpFeature>]
public sealed class SchemataIdentityFeature<TUser, TRole, TUserStore, TRoleStore> : FeatureBase
    where TUser : SchemataUser, new()
    where TRole : SchemataRole
    where TUserStore : class, IUserStore<TUser>
    where TRoleStore : class, IRoleStore<TRole>
{
    /// <summary>Default priority for identity feature startup.</summary>
    public const int DefaultPriority = Orders.Extension + 30_000_000;

    public override int Priority => DefaultPriority;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        services.AddSchemataApplicationPart<SchemataIdentityFeature<TUser, TRole, TUserStore, TRoleStore>>();
        services.AddSchemataIdentity<TUser, TRole, TUserStore, TRoleStore>(
            configurators.Pop<IdentityOptions>(),
            configurators.Pop<IdentityBuilder>());
    }
}
