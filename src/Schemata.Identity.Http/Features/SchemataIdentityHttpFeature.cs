using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Resource;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Identity.Foundation;
using Schemata.Identity.Foundation.Features;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Models;
using Schemata.Core.Building;
using Schemata.Resource.Http.Features;

namespace Schemata.Identity.Http.Features;

[DependsOn(typeof(SchemataIdentityFeature<,,,>))]
[DependsOn<SchemataHttpResourceFeature>]
public sealed class SchemataIdentityHttpFeature<TUser, TRole> : FeatureBase
    where TUser : SchemataUser, new()
    where TRole : SchemataRole
{
    public const int DefaultPriority = SchemataIdentityFeature<TUser, TRole, Skeleton.Stores.SchemataUserStore<TUser>, Skeleton.Stores.SchemataRoleStore<TRole>>.DefaultPriority + 100_000;

    public override int Priority => DefaultPriority;
    public override void ConfigureServices(
        IServiceCollection services,
        SchemataOptions schemata,
        Configurators configurators,
        IConfiguration configuration,
        IWebHostEnvironment environment
    ) {
        var resources = new SchemataResourceBuilder(schemata, services) {
            AuthenticationScheme = schemata.Get<string>(SchemataIdentityBuilder<TUser, TRole>.AuthenticationSchemeKey),
        };
        resources.Use<TUser, UserRequest, UserDetail, UserSummary>([HttpResourceAttribute.Name]);
        resources.Use<TRole, RoleRequest, RoleDetail, RoleSummary>([HttpResourceAttribute.Name]);
    }
}
