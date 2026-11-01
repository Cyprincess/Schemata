using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions;
using Schemata.Identity.Foundation;
using Schemata.Identity.Foundation.Commands;
using Schemata.Identity.Foundation.Handlers;
using Schemata.Identity.Foundation.Queries;
using Schemata.Identity.Skeleton;
using Schemata.Identity.Skeleton.Claims;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Models;
using Schemata.Messaging.Skeleton;

namespace Microsoft.AspNetCore.Builder;

public static class IdentityCapabilityExtensions
{
    public static SchemataIdentityBuilder<TUser, TRole> UseRegistration<TUser, TRole>(this SchemataIdentityBuilder<TUser, TRole> builder)
        where TUser : SchemataUser, new() where TRole : SchemataRole {
        builder.Services.TryAddScoped<IRequestHandler<RegisterUserRequest<TUser>, IdentityResult<ClaimsPrincipal>>, RegisterUserHandler<TUser>>();
        return builder;
    }
    public static SchemataIdentityBuilder<TUser, TRole> UseAccountConfirmation<TUser, TRole>(this SchemataIdentityBuilder<TUser, TRole> builder)
        where TUser : SchemataUser, new() where TRole : SchemataRole {
        builder.Services.TryAddScoped<IRequestHandler<ConfirmUserRequest<TUser>, IdentityResult<Unit>>, ConfirmUserHandler<TUser>>();
        builder.Services.TryAddScoped<IRequestHandler<SendUserConfirmationCodeRequest<TUser>, IdentityResult<Unit>>, SendUserConfirmationCodeHandler<TUser>>();
        return builder;
    }
    public static SchemataIdentityBuilder<TUser, TRole> UsePasswordReset<TUser, TRole>(this SchemataIdentityBuilder<TUser, TRole> builder)
        where TUser : SchemataUser, new() where TRole : SchemataRole {
        builder.Services.TryAddScoped<IRequestHandler<ForgotUserPasswordRequest<TUser>, IdentityResult<Unit>>, ForgotUserPasswordHandler<TUser>>();
        builder.Services.TryAddScoped<IRequestHandler<ResetUserPasswordRequest<TUser>, IdentityResult<Unit>>, ResetUserPasswordHandler<TUser>>();
        return builder;
    }
    public static SchemataIdentityBuilder<TUser, TRole> UsePasswordChange<TUser, TRole>(this SchemataIdentityBuilder<TUser, TRole> builder)
        where TUser : SchemataUser, new() where TRole : SchemataRole {
        builder.Services.TryAddScoped<IRequestHandler<ChangeUserPasswordRequest<TUser>, IdentityResult<Unit>>, ChangeUserPasswordHandler<TUser>>();
        return builder;
    }
    public static SchemataIdentityBuilder<TUser, TRole> UseEmailChange<TUser, TRole>(this SchemataIdentityBuilder<TUser, TRole> builder)
        where TUser : SchemataUser, new() where TRole : SchemataRole {
        builder.Services.TryAddScoped<IRequestHandler<ChangeUserEmailRequest<TUser>, IdentityResult<Unit>>, ChangeUserEmailHandler<TUser>>();
        builder.Services.TryAddScoped<IRequestHandler<ConfirmUserRequest<TUser>, IdentityResult<Unit>>, ConfirmUserHandler<TUser>>();
        return builder;
    }
    public static SchemataIdentityBuilder<TUser, TRole> UsePhoneNumberChange<TUser, TRole>(this SchemataIdentityBuilder<TUser, TRole> builder)
        where TUser : SchemataUser, new() where TRole : SchemataRole {
        builder.Services.TryAddScoped<IRequestHandler<ChangeUserPhoneRequest<TUser>, IdentityResult<Unit>>, ChangeUserPhoneHandler<TUser>>();
        builder.Services.TryAddScoped<IRequestHandler<ConfirmUserRequest<TUser>, IdentityResult<Unit>>, ConfirmUserHandler<TUser>>();
        return builder;
    }
    public static SchemataIdentityBuilder<TUser, TRole> UseTwoFactorAuthentication<TUser, TRole>(this SchemataIdentityBuilder<TUser, TRole> builder)
        where TUser : SchemataUser, new() where TRole : SchemataRole {
        builder.Services.TryAddScoped<IRequestHandler<GetUserAuthenticatorRequest<TUser>, IdentityResult<AuthenticatorResponse>>, GetUserAuthenticatorHandler<TUser>>();
        builder.Services.TryAddScoped<IRequestHandler<EnrollUserAuthenticatorRequest<TUser>, IdentityResult<Unit>>, EnrollUserAuthenticatorHandler<TUser>>();
        builder.Services.TryAddScoped<IRequestHandler<DowngradeUserAuthenticatorRequest<TUser>, IdentityResult<Unit>>, DowngradeUserAuthenticatorHandler<TUser>>();
        return builder;
    }
}
