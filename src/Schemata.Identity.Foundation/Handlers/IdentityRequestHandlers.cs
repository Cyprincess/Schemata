using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Identity.Foundation.Commands;
using Schemata.Identity.Foundation.Queries;
using Schemata.Identity.Skeleton;
using Schemata.Identity.Skeleton.Claims;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Models;
using Schemata.Messaging.Skeleton;

namespace Schemata.Identity.Foundation.Handlers;

internal sealed class RegisterUserHandler<TUser>(IdentityOperationHandler<TUser> operations)
    : IRequestHandler<RegisterUserRequest<TUser>, IdentityResult<ClaimsPrincipal>>
    where TUser : SchemataUser, new()
{
    public Task<IdentityResult<ClaimsPrincipal>> HandleAsync(
        RegisterUserRequest<TUser> request,
        CancellationToken          ct = default
    ) => operations.RegisterAsync(IdentityRequestHandler.Require(request).Request, request.Principal!, ct);
}

internal sealed class LoginUserHandler<TUser>(IdentityOperationHandler<TUser> operations)
    : IRequestHandler<LoginUserRequest<TUser>, IdentityResult<ClaimsPrincipal>>
    where TUser : SchemataUser, new()
{
    public Task<IdentityResult<ClaimsPrincipal>> HandleAsync(
        LoginUserRequest<TUser> request,
        CancellationToken       ct = default
    ) => operations.LoginAsync(IdentityRequestHandler.Require(request).Request, request.Principal!, ct);
}

internal sealed class RefreshUserHandler<TUser>(IdentityOperationHandler<TUser> operations)
    : IRequestHandler<RefreshUserRequest<TUser>, IdentityResult<ClaimsPrincipal>>
    where TUser : SchemataUser, new()
{
    public Task<IdentityResult<ClaimsPrincipal>> HandleAsync(
        RefreshUserRequest<TUser> request,
        CancellationToken         ct = default
    ) => operations.RefreshAsync(IdentityRequestHandler.Require(request).Ticket, request.Principal!, ct);
}

internal sealed class GetUserProfileHandler<TUser>(IdentityOperationHandler<TUser> operations)
    : IRequestHandler<GetUserProfileQuery<TUser>, IdentityResult<ClaimsStore>>
    where TUser : SchemataUser, new()
{
    public Task<IdentityResult<ClaimsStore>> HandleAsync(
        GetUserProfileQuery<TUser> request,
        CancellationToken           ct = default
    ) => operations.ProfileAsync(IdentityRequestHandler.Require(request).Principal!, ct);
}

internal sealed class ChangeUserEmailHandler<TUser>(IdentityOperationHandler<TUser> operations)
    : IRequestHandler<ChangeUserEmailRequest<TUser>, IdentityResult<Unit>>
    where TUser : SchemataUser, new()
{
    public Task<IdentityResult<Unit>> HandleAsync(
        ChangeUserEmailRequest<TUser> request,
        CancellationToken             ct = default
    ) => operations.ChangeEmailAsync(IdentityRequestHandler.Require(request).Request, request.Principal!, ct);
}

internal sealed class ChangeUserPhoneHandler<TUser>(IdentityOperationHandler<TUser> operations)
    : IRequestHandler<ChangeUserPhoneRequest<TUser>, IdentityResult<Unit>>
    where TUser : SchemataUser, new()
{
    public Task<IdentityResult<Unit>> HandleAsync(
        ChangeUserPhoneRequest<TUser> request,
        CancellationToken             ct = default
    ) => operations.ChangePhoneAsync(IdentityRequestHandler.Require(request).Request, request.Principal!, ct);
}

internal sealed class ChangeUserPasswordHandler<TUser>(IdentityOperationHandler<TUser> operations)
    : IRequestHandler<ChangeUserPasswordRequest<TUser>, IdentityResult<Unit>>
    where TUser : SchemataUser, new()
{
    public Task<IdentityResult<Unit>> HandleAsync(
        ChangeUserPasswordRequest<TUser> request,
        CancellationToken                ct = default
    ) => operations.ChangePasswordAsync(IdentityRequestHandler.Require(request).Request, request.Principal!, ct);
}

internal sealed class ForgotUserPasswordHandler<TUser>(IdentityOperationHandler<TUser> operations)
    : IRequestHandler<ForgotUserPasswordRequest<TUser>, IdentityResult<Unit>>
    where TUser : SchemataUser, new()
{
    public Task<IdentityResult<Unit>> HandleAsync(
        ForgotUserPasswordRequest<TUser> request,
        CancellationToken                ct = default
    ) => operations.ForgotAsync(IdentityRequestHandler.Require(request).Request, request.Principal!, ct);
}

internal sealed class ResetUserPasswordHandler<TUser>(IdentityOperationHandler<TUser> operations)
    : IRequestHandler<ResetUserPasswordRequest<TUser>, IdentityResult<Unit>>
    where TUser : SchemataUser, new()
{
    public Task<IdentityResult<Unit>> HandleAsync(
        ResetUserPasswordRequest<TUser> request,
        CancellationToken               ct = default
    ) => operations.ResetAsync(IdentityRequestHandler.Require(request).Request, request.Principal!, ct);
}

internal sealed class ConfirmUserHandler<TUser>(IdentityOperationHandler<TUser> operations)
    : IRequestHandler<ConfirmUserRequest<TUser>, IdentityResult<Unit>>
    where TUser : SchemataUser, new()
{
    public Task<IdentityResult<Unit>> HandleAsync(
        ConfirmUserRequest<TUser> request,
        CancellationToken         ct = default
    ) => operations.ConfirmAsync(IdentityRequestHandler.Require(request).Request, request.Principal!, ct);
}

internal sealed class SendUserConfirmationCodeHandler<TUser>(IdentityOperationHandler<TUser> operations)
    : IRequestHandler<SendUserConfirmationCodeRequest<TUser>, IdentityResult<Unit>>
    where TUser : SchemataUser, new()
{
    public Task<IdentityResult<Unit>> HandleAsync(
        SendUserConfirmationCodeRequest<TUser> request,
        CancellationToken                      ct = default
    ) => operations.CodeAsync(IdentityRequestHandler.Require(request).Request, request.Principal!, ct);
}

internal sealed class GetUserAuthenticatorHandler<TUser>(IdentityOperationHandler<TUser> operations)
    : IRequestHandler<GetUserAuthenticatorRequest<TUser>, IdentityResult<AuthenticatorResponse>>
    where TUser : SchemataUser, new()
{
    public Task<IdentityResult<AuthenticatorResponse>> HandleAsync(
        GetUserAuthenticatorRequest<TUser> request,
        CancellationToken                  ct = default
    ) => operations.AuthenticatorAsync(IdentityRequestHandler.Require(request).Principal!, ct);
}

internal sealed class EnrollUserAuthenticatorHandler<TUser>(IdentityOperationHandler<TUser> operations)
    : IRequestHandler<EnrollUserAuthenticatorRequest<TUser>, IdentityResult<Unit>>
    where TUser : SchemataUser, new()
{
    public Task<IdentityResult<Unit>> HandleAsync(
        EnrollUserAuthenticatorRequest<TUser> request,
        CancellationToken                     ct = default
    ) => operations.EnrollAsync(IdentityRequestHandler.Require(request).Request, request.Principal!, ct);
}

internal sealed class DowngradeUserAuthenticatorHandler<TUser>(IdentityOperationHandler<TUser> operations)
    : IRequestHandler<DowngradeUserAuthenticatorRequest<TUser>, IdentityResult<Unit>>
    where TUser : SchemataUser, new()
{
    public Task<IdentityResult<Unit>> HandleAsync(
        DowngradeUserAuthenticatorRequest<TUser> request,
        CancellationToken                        ct = default
    ) => operations.DowngradeAsync(IdentityRequestHandler.Require(request).Request, request.Principal!, ct);
}

internal static class IdentityRequestHandler
{
    internal static T Require<T>(T? request) where T : class {
        ArgumentNullException.ThrowIfNull(request);
        return request;
    }
}
