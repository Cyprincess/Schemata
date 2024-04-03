using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Schemata.Core;
using Schemata.Core.Features;

namespace Schemata.Identity.Foundation.Features;

[DependsOn<SchemataControllersFeature>]
[Information("Identity depends on Controllers feature, it will be added automatically.", Level = LogLevel.Debug)]
public class SchemataIdentityFeature<TUser, TRole, TUserStore, TRoleStore> : FeatureBase
    where TUser : class
    where TRole : class
    where TUserStore : class, IUserStore<TUser>
    where TRoleStore : class, IRoleStore<TRole>
{
    public override int Priority => 310_000_000;

    public override void ConfigureServices(
        IServiceCollection  services,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment) {
        var configure = configurators.Pop<IdentityOptions>();

        var build = configurators.Pop<IdentityBuilder>();

        services.TryAddScoped<IUserStore<TUser>, TUserStore>();
        services.TryAddScoped<IRoleStore<TRole>, TRoleStore>();

        var builder = services.AddIdentity<TUser, TRole>(configure)
                .AddDefaultTokenProviders();

        build(builder);
    }
}
