using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Identity.Skeleton.Entities;
#if NET6_0
using Microsoft.AspNetCore.Authentication.BearerToken;
#endif

namespace Schemata.Identity.Foundation.Features;

[DependsOn<SchemataAuthenticationFeature>]
[DependsOn<SchemataControllersFeature>]
[Information("Identity depends on Authentication and Controllers features, these features will be added automatically.", Level = LogLevel.Debug)]
public class SchemataIdentityFeature<TUser, TRole, TUserStore, TRoleStore> : FeatureBase
    where TUser : SchemataUser
    where TRole : SchemataRole
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

        services.AddAuthentication(IdentityConstants.ApplicationScheme)
                .AddBearerToken(IdentityConstants.ApplicationScheme);

        services.TryAddScoped<IUserStore<TUser>, TUserStore>();
        services.TryAddScoped<IRoleStore<TRole>, TRoleStore>();

        var builder = services.AddIdentityCore<TUser>(configure)
                              .AddRoles<TRole>()
                              .AddSignInManager()
                              .AddDefaultTokenProviders();

        build(builder);
    }

    public override void ConfigureEndpoints(
        IEndpointRouteBuilder endpoints,
        IConfiguration        configuration,
        IWebHostEnvironment   environment) {
        endpoints.UseIdentity<TUser, TRole>();
    }
}
