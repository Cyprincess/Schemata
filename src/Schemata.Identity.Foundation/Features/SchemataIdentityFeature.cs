using System;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Options;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Json;
using Schemata.Identity.Skeleton.Managers;
using Schemata.Identity.Skeleton.Services;

namespace Schemata.Identity.Foundation.Features;

[DependsOn<SchemataAuthenticationFeature>]
[DependsOn<SchemataControllersFeature>]
public sealed class SchemataIdentityFeature<TUser, TRole, TUserStore, TRoleStore> : FeatureBase
    where TUser : SchemataUser
    where TRole : SchemataRole
    where TUserStore : class, IUserStore<TUser>
    where TRoleStore : class, IRoleStore<TRole>
{
    public override int Priority => 310_000_000;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment) {
        var configure = configurators.Pop<IdentityOptions>();
        var build     = configurators.Pop<IdentityBuilder>();
        var bearer    = configurators.Pop<BearerTokenOptions>();

        var identify = configurators.Pop<SchemataIdentityOptions>();
        services.Configure(identify);

        var converter = new ClaimStoreJsonConverter();

        services.Configure<JsonSerializerOptions>(options => {
            options.Converters.Add(converter);
        });

        services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(options => {
            options.SerializerOptions.Converters.Add(converter);
        });

        services.Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(options => {
            options.JsonSerializerOptions.Converters.Add(converter);
        });

        var part = new SchemataExtensionPart<SchemataIdentityFeature<TUser, TRole, TUserStore, TRoleStore>>();
        services.AddMvcCore()
                .ConfigureApplicationPartManager(manager => { manager.ApplicationParts.Add(part); });

        services.AddMemoryCache();

        services.AddAuthentication("Identity.BearerAndApplication")
                .AddScheme<AuthenticationSchemeOptions, CompositeIdentityHandler>("Identity.BearerAndApplication", null, composite => {
                     composite.ForwardDefault      = IdentityConstants.BearerScheme;
                     composite.ForwardAuthenticate = "Identity.BearerAndApplication";
                 })
                .AddBearerToken(IdentityConstants.BearerScheme, bearer)
                .AddIdentityCookies();

        services.TryAddTransient(typeof(IMailSender<>), typeof(NoOpMailSender<>));
        services.TryAddTransient(typeof(IMessageSender<>), typeof(NoOpMessageSender<>));

        services.TryAddScoped<IUserStore<TUser>, TUserStore>();
        services.TryAddScoped<IRoleStore<TRole>, TRoleStore>();

        var builder = services.AddIdentityCore<TUser>(configure)
                              .AddRoles<TRole>()
                              .AddUserManager<SchemataUserManager<TUser>>()
                              .AddSignInManager()
                              .AddDefaultTokenProviders();

        build(builder);
    }

    private sealed class CompositeIdentityHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : SignInAuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override async Task<AuthenticateResult> HandleAuthenticateAsync() {
            var result = await Context.AuthenticateAsync(IdentityConstants.BearerScheme);

            if (!result.None) {
                return result;
            }

            return await Context.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        }

        protected override Task HandleSignInAsync(ClaimsPrincipal user, AuthenticationProperties? properties) {
            throw new NotImplementedException();
        }

        protected override Task HandleSignOutAsync(AuthenticationProperties? properties) {
            throw new NotImplementedException();
        }
    }
}
