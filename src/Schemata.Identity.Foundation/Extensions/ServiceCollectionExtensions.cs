using System;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions;
using Schemata.Identity.Foundation;
using Schemata.Identity.Foundation.Advisors;
using Schemata.Identity.Foundation.Commands;
using Schemata.Identity.Foundation.Controllers;
using Schemata.Identity.Foundation.Handlers;
using Schemata.Identity.Foundation.Internal;
using Schemata.Identity.Foundation.Queries;
using Schemata.Identity.Skeleton;
using Schemata.Identity.Skeleton.Advisors;
using Schemata.Identity.Skeleton.Claims;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Json;
using Schemata.Identity.Skeleton.Managers;
using Schemata.Identity.Skeleton.Models;
using Schemata.Identity.Skeleton.Services;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Internal;
using static Schemata.Abstractions.SchemataConstants;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///     Extension methods registering Schemata's Identity-backed API surface.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    ///     Registers the Identity API endpoints, the Schemata user manager and claims-principal
    ///     factory, the request-advisor chain, the default no-op mail and message senders, and the
    ///     claim-store JSON converter.
    /// </summary>
    /// <typeparam name="TUser">User entity type.</typeparam>
    /// <typeparam name="TRole">Role entity type.</typeparam>
    /// <typeparam name="TUserStore">User store implementation type.</typeparam>
    /// <typeparam name="TRoleStore">Role store implementation type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Identity options configuration.</param>
    /// <param name="build">Applied to the <see cref="IdentityBuilder" /> once it is assembled.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSchemataIdentity<TUser, TRole, TUserStore, TRoleStore>(
        this IServiceCollection services,
        Action<IdentityOptions> configure,
        Action<IdentityBuilder> build
    )
        where TUser : SchemataUser, new()
        where TRole : SchemataRole
        where TUserStore : class, IUserStore<TUser>
        where TRoleStore : class, IRoleStore<TRole> {
        services.Configure<JsonSerializerOptions>(options => {
            options.Converters.Add(ClaimStoreJsonConverter.Instance);
        });

        services.Configure<JsonOptions>(options => {
            options.SerializerOptions.Converters.Add(ClaimStoreJsonConverter.Instance);
        });

        services.Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(options => {
            options.JsonSerializerOptions.Converters.Add(ClaimStoreJsonConverter.Instance);
        });

        services.AddMvcCore()
                .ConfigureApplicationPartManager(manager => {
                     manager.FeatureProviders.Add(new IdentityControllerFeatureProvider(typeof(AuthenticateController<TUser>)));
                 });

        services.TryAddScoped<InProcessRequestDispatcher>();
        services.TryAddScoped<IRequestDispatcher>(sp => sp.GetRequiredService<InProcessRequestDispatcher>());
        services.TryAddScoped<ICommandDispatcher>(sp => sp.GetRequiredService<InProcessRequestDispatcher>());
        services.TryAddScoped<IQueryDispatcher>(sp => sp.GetRequiredService<InProcessRequestDispatcher>());
        services.TryAddScoped<IdentityOperationHandler<TUser>>();
        AddIdentityHandlers<TUser>(services);
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
            o.ClaimsIdentity.UserIdClaimType        = IdentityClaims.Subject;
            o.ClaimsIdentity.UserNameClaimType      = IdentityClaims.PreferredUsername;
            o.ClaimsIdentity.EmailClaimType         = IdentityClaims.Email;
            o.ClaimsIdentity.RoleClaimType          = IdentityClaims.Role;
            o.ClaimsIdentity.SecurityStampClaimType = IdentityClaims.SecurityStamp;
        });

        var builder = services.AddIdentityApiEndpoints<TUser>(configure)
                              .AddRoles<TRole>()
                              .AddUserManager<SchemataUserManager<TUser>>()
                              .AddClaimsPrincipalFactory<SchemataUserClaimsPrincipalFactory<TUser, TRole>>();

        // Registered after AddIdentityApiEndpoints so this assignment is the last one applied to the
        // application cookie's redirect event.
        services.Configure<CookieAuthenticationOptions>(
            IdentityConstants.ApplicationScheme,
            o => o.Events.OnRedirectToLogin = LoginContinuation.RedirectToLoginAsync);

        build(builder);

        return services;
    }

    private static void AddIdentityHandlers<TUser>(IServiceCollection services)
        where TUser : SchemataUser, new() {
        var user = typeof(TUser);
        var principalResult = typeof(IdentityResult<ClaimsPrincipal>);
        var unitResult      = typeof(IdentityResult<Unit>);
        AddHandler(services, typeof(RegisterUserRequest<>), principalResult, typeof(RegisterUserHandler<>), user);
        AddHandler(services, typeof(LoginUserRequest<>), principalResult, typeof(LoginUserHandler<>), user);
        AddHandler(services, typeof(RefreshUserRequest<>), principalResult, typeof(RefreshUserHandler<>), user);
        AddHandler(
            services,
            typeof(GetUserProfileQuery<>),
            typeof(IdentityResult<ClaimsStore>),
            typeof(GetUserProfileHandler<>),
            user);
        AddHandler(services, typeof(ChangeUserEmailRequest<>), unitResult, typeof(ChangeUserEmailHandler<>), user);
        AddHandler(services, typeof(ChangeUserPhoneRequest<>), unitResult, typeof(ChangeUserPhoneHandler<>), user);
        AddHandler(services, typeof(ChangeUserPasswordRequest<>), unitResult, typeof(ChangeUserPasswordHandler<>), user);
        AddHandler(services, typeof(ForgotUserPasswordRequest<>), unitResult, typeof(ForgotUserPasswordHandler<>), user);
        AddHandler(services, typeof(ResetUserPasswordRequest<>), unitResult, typeof(ResetUserPasswordHandler<>), user);
        AddHandler(services, typeof(ConfirmUserRequest<>), unitResult, typeof(ConfirmUserHandler<>), user);
        AddHandler(
            services,
            typeof(SendUserConfirmationCodeRequest<>),
            unitResult,
            typeof(SendUserConfirmationCodeHandler<>),
            user);
        AddHandler(
            services,
            typeof(GetUserAuthenticatorRequest<>),
            typeof(IdentityResult<AuthenticatorResponse>),
            typeof(GetUserAuthenticatorHandler<>),
            user);
        AddHandler(
            services,
            typeof(EnrollUserAuthenticatorRequest<>),
            unitResult,
            typeof(EnrollUserAuthenticatorHandler<>),
            user);
        AddHandler(
            services,
            typeof(DowngradeUserAuthenticatorRequest<>),
            unitResult,
            typeof(DowngradeUserAuthenticatorHandler<>),
            user);
    }

    private static void AddHandler(
        IServiceCollection services,
        Type               request,
        Type               response,
        Type               handler,
        Type               user
    ) {
        services.TryAddScoped(
            typeof(IRequestHandler<,>).MakeGenericType(request.MakeGenericType(user), response),
            handler.MakeGenericType(user));
    }

}
