using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Identity.Foundation.Advisors;
using Schemata.Identity.Foundation.Controllers;
using Schemata.Identity.Foundation.Handlers;
using Schemata.Identity.Skeleton.Advisors;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Json;
using Schemata.Identity.Skeleton.Managers;
using Schemata.Identity.Skeleton.Models;
using Schemata.Identity.Skeleton.Services;
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
        var configure = configurators.Pop<IdentityOptions>();
        var build     = configurators.Pop<IdentityBuilder>();

        services.Configure<JsonSerializerOptions>(options => {
            options.Converters.Add(ClaimStoreJsonConverter.Instance);
        });

        services.Configure<JsonOptions>(options => {
            options.SerializerOptions.Converters.Add(ClaimStoreJsonConverter.Instance);
        });

        services.Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(options => {
            options.JsonSerializerOptions.Converters.Add(ClaimStoreJsonConverter.Instance);
        });

        services.AddSchemataApplicationPart<SchemataIdentityFeature<TUser, TRole, TUserStore, TRoleStore>>();
        services.AddMvcCore()
                .ConfigureApplicationPartManager(manager => {
                     manager.FeatureProviders.Add(new IdentityControllerFeatureProvider(typeof(AuthenticateController<TUser>)));
                 });

        services.TryAddScoped<IdentityHandler<TUser>>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IIdentityRequestAdvisor<>), typeof(AdviceRequestFeature<>)));

        services.TryAddEnumerable(ServiceDescriptor.Scoped<IIdentityRequestAdvisor<ConfirmRequest>, AdviceRequestConfirmValidation>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IIdentityRequestAdvisor<ProfileRequest>, AdviceRequestEmailValidation<TUser>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IIdentityRequestAdvisor<ProfileRequest>, AdviceRequestPhoneValidation<TUser>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IIdentityRequestAdvisor<ProfileRequest>, AdviceRequestPasswordValidation<TUser>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IIdentityRequestAdvisor<AuthenticatorRequest>, AdviceRequestEnrollValidation<TUser>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IIdentityRequestAdvisor<AuthenticatorRequest>, AdviceRequestDowngradeValidation>());

        services.TryAddScoped(typeof(IMailSender<>), typeof(NoOpMailSender<>));
        services.TryAddScoped(typeof(IMessageSender<>), typeof(NoOpMessageSender<>));

        services.TryAddScoped<IUserStore<TUser>, TUserStore>();
        services.TryAddScoped<IRoleStore<TRole>, TRoleStore>();

        services.Configure<IdentityOptions>(o => {
            o.ClaimsIdentity.UserIdClaimType        = Claims.Subject;
            o.ClaimsIdentity.UserNameClaimType      = Claims.PreferredUsername;
            o.ClaimsIdentity.EmailClaimType         = Claims.Email;
            o.ClaimsIdentity.RoleClaimType          = Claims.Role;
            o.ClaimsIdentity.SecurityStampClaimType = Claims.SecurityStamp;
        });

        var builder = services.AddIdentityApiEndpoints<TUser>(configure)
                              .AddRoles<TRole>()
                              .AddUserManager<SchemataUserManager<TUser>>()
                              .AddClaimsPrincipalFactory<SchemataUserClaimsPrincipalFactory<TUser, TRole>>();

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
