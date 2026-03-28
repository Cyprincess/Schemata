using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Identity.Foundation.Advisors;
using Schemata.Identity.Foundation.Controllers;
using Schemata.Identity.Foundation.Handlers;
using Schemata.Identity.Foundation.Services;
using Schemata.Identity.Skeleton.Advisors;
using Schemata.Identity.Skeleton.Claims;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Managers;
using Schemata.Identity.Skeleton.Models;
using Schemata.Identity.Skeleton.Services;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Identity.Foundation.Features;

[DependsOn<SchemataAuthenticationFeature>]
[DependsOn<SchemataControllersFeature>]
public sealed class SchemataIdentityFeature<TUser, TRole, TUserStore, TRoleStore> : FeatureBase
    where TUser : SchemataUser, new()
    where TRole : SchemataRole
    where TUserStore : class, IUserStore<TUser>
    where TRoleStore : class, IRoleStore<TRole>
{
    public const int DefaultPriority = Orders.Extension + 10_000_000;

    /// <inheritdoc />
    public override int Priority => DefaultPriority;

    /// <inheritdoc />
    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment
    ) {
        var configure = configurators.Pop<IdentityOptions>();
        var build     = configurators.Pop<IdentityBuilder>();

        var identify = configurators.Pop<SchemataIdentityOptions>();
        var opts     = new SchemataIdentityOptions();
        identify(opts);
        services.Configure(identify);

        var part = new SchemataExtensionPart<SchemataIdentityFeature<TUser, TRole, TUserStore, TRoleStore>>();
        services.AddMvcCore()
                .ConfigureApplicationPartManager(manager => {
                     manager.ApplicationParts.Add(part);
                     manager.FeatureProviders.Add(new IdentityControllerFeatureProvider(typeof(AuthenticateController<TUser>)));
                 });

        // Handler
        services.TryAddScoped<IdentityHandler<TUser>>();

        // Advisors
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IIdentityRequestAdvisor<>), typeof(AdviceIdentityFeatureGate<>)));

        services.TryAddEnumerable(ServiceDescriptor.Scoped<IIdentityRequestAdvisor<ConfirmRequest>, AdviceConfirmRequestValidation>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IIdentityRequestAdvisor<ProfileRequest>, AdviceChangeEmailValidation<TUser>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IIdentityRequestAdvisor<ProfileRequest>, AdviceChangePhoneValidation<TUser>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IIdentityRequestAdvisor<ProfileRequest>, AdviceChangePasswordValidation<TUser>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IIdentityRequestAdvisor<AuthenticatorRequest>, AdviceEnrollValidation<TUser>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IIdentityRequestAdvisor<AuthenticatorRequest>, AdviceDowngradeValidation>());

        // Claims and services
        services.TryAddScoped<IClaimsProvider<TUser>, DefaultClaimsProvider<TUser>>();

        services.TryAddScoped(typeof(IMailSender<>), typeof(NoOpMailSender<>));
        services.TryAddScoped(typeof(IMessageSender<>), typeof(NoOpMessageSender<>));

        // Identity stores and managers
        services.TryAddScoped<IUserStore<TUser>, TUserStore>();
        services.TryAddScoped<IRoleStore<TRole>, TRoleStore>();

        services.Configure<IdentityOptions>(o => {
            o.ClaimsIdentity.UserIdClaimType        = SchemataConstants.Claims.Subject;
            o.ClaimsIdentity.UserNameClaimType      = SchemataConstants.Claims.PreferredUsername;
            o.ClaimsIdentity.EmailClaimType         = SchemataConstants.Claims.Email;
            o.ClaimsIdentity.RoleClaimType          = SchemataConstants.Claims.Role;
            o.ClaimsIdentity.SecurityStampClaimType = SchemataConstants.Claims.SecurityStamp;
        });

        var builder = services.AddIdentityApiEndpoints<TUser>(configure)
                              .AddRoles<TRole>()
                              .AddUserManager<SchemataUserManager<TUser>>();

        build(builder);
    }

    #region Nested type: IdentityControllerFeatureProvider

    private sealed class IdentityControllerFeatureProvider(Type controllerType) : IApplicationFeatureProvider<ControllerFeature>
    {
        #region IApplicationFeatureProvider<ControllerFeature> Members

        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature) {
            var typeInfo = controllerType.GetTypeInfo();
            if (!feature.Controllers.Contains(typeInfo)) {
                feature.Controllers.Add(typeInfo);
            }
        }

        #endregion
    }

    #endregion
}
