using System;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions;
using Schemata.Entity.Repository;
using Schemata.Identity.Foundation;
using Schemata.Identity.Foundation.Advisors;
using Schemata.Identity.Foundation.Commands;
using Schemata.Identity.Foundation.Controllers;
using Schemata.Identity.Foundation.Handlers;
using Schemata.Identity.Foundation.Queries;
using Schemata.Identity.Foundation.Runtime;
using Schemata.Identity.Foundation.Services;
using Schemata.Identity.Skeleton;
using Schemata.Identity.Skeleton.Advisors;
using Schemata.Identity.Skeleton.Claims;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Json;
using Schemata.Identity.Skeleton.Managers;
using Schemata.Identity.Skeleton.Mutations;
using Schemata.Identity.Skeleton.Models;
using Schemata.Identity.Skeleton.Services;
using Schemata.Messaging.Skeleton;
using static Schemata.Abstractions.SchemataConstants;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
///     Extension methods registering Schemata's Identity-backed API surface.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    ///     Registers the closed Identity execution graph for one user type — the in-process
    ///     dispatcher, the operation handler, each closed request handler, and the request
    ///     handler slot — into <paramref name="services" />. The global registration calls this
    ///     entry, and a tenant execution configure calls the same entry so tenant requests
    ///     resolve these services from the tenant boundary; the advisor chain, stores, and
    ///     mutation owners stay host-owned because they are shared collaborators, not
    ///     per-tenant executions.
    /// </summary>
    /// <typeparam name="TUser">User entity type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddIdentityExecution<TUser>(this IServiceCollection services)
        where TUser : SchemataUser, new() {
        services.AddInProcessRequestDispatcher();
        services.TryAddScoped<IdentityOperationHandler<TUser>>();
        AddIdentityHandlers<TUser>(services);
        services.TryAddScoped<IdentityHandler<TUser>>();

        return services;
    }

    /// <summary>
    ///     Registers the Identity API endpoints, the Schemata user manager and claims-principal
    ///     factory, the request-advisor chain, the closed stores and mutation owners, the default
    ///     no-op mail and message senders, and the claim-store JSON converter.
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
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Identity options configuration.</param>
    /// <param name="build">Applied to the <see cref="IdentityBuilder" /> once it is assembled.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSchemataIdentity<TUser, TRole, TUserClaim, TRoleClaim, TUserLogin, TUserToken,
        TUserRole, TUserStore, TRoleStore>(
        this IServiceCollection services,
        Action<IdentityOptions> configure,
        Action<IdentityBuilder> build
    )
        where TUser : SchemataUser, new()
        where TRole : SchemataRole, new()
        where TUserClaim : SchemataUserClaim, new()
        where TRoleClaim : SchemataRoleClaim, new()
        where TUserLogin : SchemataUserLogin, new()
        where TUserToken : SchemataUserToken, new()
        where TUserRole : SchemataUserRole, new()
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
        services.AddOptions<Microsoft.AspNetCore.Mvc.MvcOptions>()
            .Configure<IServiceProviderIsService>((mvc, installed) => mvc.Conventions.Add(new IdentityControllerConvention<TUser>(installed)));

        AddIdentityExecution<TUser>(services);

        services.TryAddEnumerable(ServiceDescriptor.Scoped<IIdentityRequestAdvisor<ConfirmRequest, IdentityResult<Unit>>, AdviceRequestConfirmValidation>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IIdentityRequestAdvisor<ProfileRequest, IdentityResult<Unit>>, AdviceRequestEmailValidation<TUser>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IIdentityRequestAdvisor<ProfileRequest, IdentityResult<Unit>>, AdviceRequestPhoneValidation<TUser>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IIdentityRequestAdvisor<ProfileRequest, IdentityResult<Unit>>, AdviceRequestPasswordValidation<TUser>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IIdentityRequestAdvisor<AuthenticatorRequest, IdentityResult<Unit>>, AdviceRequestEnrollValidation<TUser>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IIdentityRequestAdvisor<AuthenticatorRequest, IdentityResult<Unit>>, AdviceRequestDowngradeValidation>());

        services.TryAddScoped<IUserStore<TUser>, TUserStore>();
        services.TryAddScoped<IRoleStore<TRole>, TRoleStore>();
        services.TryAddScoped<IResourceMutation<TUser>, UserResourceMutation<TUser, TUserClaim, TUserRole, TUserLogin, TUserToken>>();
        services.TryAddScoped<IResourceMutation<TRole>, RoleResourceMutation<TRole, TRoleClaim, TUserRole>>();

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
                              .AddErrorDescriber<SchemataErrorDescriber>()
                              .AddSignInManager<SchemataSignInManager<TUser>>()
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
        AddHandler(services, typeof(LoginUserRequest<>), unitResult, typeof(LoginUserHandler<>), user);
        AddHandler(services, typeof(RefreshUserRequest<>), principalResult, typeof(RefreshUserHandler<>), user);
        AddHandler(
            services,
            typeof(GetUserProfileQuery<>),
            typeof(IdentityResult<ClaimsStore>),
            typeof(GetUserProfileHandler<>),
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
