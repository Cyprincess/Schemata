using System;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;
using Schemata.Identity.Foundation.Commands;
using Schemata.Identity.Foundation.Controllers;
using Schemata.Identity.Foundation.Queries;
using Schemata.Identity.Skeleton;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Models;
using Schemata.Messaging.Skeleton;

namespace Schemata.Identity.Foundation.Runtime;

internal sealed class IdentityControllerConvention<TUser>(IServiceProviderIsService services) : IControllerModelConvention
    where TUser : SchemataUser, new()
{
    public void Apply(ControllerModel controller) {
        if (controller.ControllerType != typeof(AuthenticateController<TUser>)) return;
        for (var i = controller.Actions.Count - 1; i >= 0; i--) {
            var type = controller.Actions[i].ActionName switch {
                "Register" => typeof(IRequestHandler<RegisterUserRequest<TUser>, IdentityResult<ClaimsPrincipal>>),
                "Confirm" => typeof(IRequestHandler<ConfirmUserRequest<TUser>, IdentityResult<Unit>>),
                "Code" => typeof(IRequestHandler<SendUserConfirmationCodeRequest<TUser>, IdentityResult<Unit>>),
                "Forgot" => typeof(IRequestHandler<ForgotUserPasswordRequest<TUser>, IdentityResult<Unit>>),
                "Reset" => typeof(IRequestHandler<ResetUserPasswordRequest<TUser>, IdentityResult<Unit>>),
                "Email" => typeof(IRequestHandler<ChangeUserEmailRequest<TUser>, IdentityResult<Unit>>),
                "Phone" => typeof(IRequestHandler<ChangeUserPhoneRequest<TUser>, IdentityResult<Unit>>),
                "Password" => typeof(IRequestHandler<ChangeUserPasswordRequest<TUser>, IdentityResult<Unit>>),
                "Authenticator" => typeof(IRequestHandler<GetUserAuthenticatorRequest<TUser>, IdentityResult<AuthenticatorResponse>>),
                "Enroll" => typeof(IRequestHandler<EnrollUserAuthenticatorRequest<TUser>, IdentityResult<Unit>>),
                "Downgrade" => typeof(IRequestHandler<DowngradeUserAuthenticatorRequest<TUser>, IdentityResult<Unit>>),
                _ => null,
            };
            if (type is not null && !services.IsService(type)) controller.Actions.RemoveAt(i);
        }
    }
}
