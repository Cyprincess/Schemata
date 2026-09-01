using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;
using Schemata.Identity.Foundation.Commands;
using Schemata.Identity.Foundation.Queries;
using Schemata.Identity.Skeleton;
using Schemata.Identity.Skeleton.Claims;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Managers;
using Schemata.Identity.Skeleton.Models;
using Schemata.Identity.Skeleton.Services;
using Schemata.Messaging.Skeleton;

namespace Schemata.Identity.Foundation.Handlers;

/// <summary>Dispatcher-backed facade for identity operations.</summary>
/// <typeparam name="TUser">User entity type handled by the identity pipeline.</typeparam>
public sealed class IdentityHandler<TUser>
    where TUser : SchemataUser, new()
{
    private readonly IRequestDispatcher _dispatcher;

    /// <summary>Creates a dispatcher-backed identity facade.</summary>
    public IdentityHandler(
        SchemataUserManager<TUser> users,
        SignInManager<TUser>       sign,
        IMailSender<TUser>         mail,
        IMessageSender<TUser>      message,
        IServiceProvider           sp
    ) {
        _ = users;
        _ = sign;
        _ = mail;
        _ = message;
        _dispatcher = sp.GetRequiredService<IRequestDispatcher>();
    }

    /// <summary>Registers a user and builds the sign-in principal.</summary>
    public Task<IdentityResult<ClaimsPrincipal>> RegisterAsync(
        RegisterRequest   request,
        ClaimsPrincipal   principal,
        CancellationToken ct = default
    ) => _dispatcher.SendAsync<RegisterUserRequest<TUser>, IdentityResult<ClaimsPrincipal>>(
        new(request, principal), ct);

    /// <summary>Authenticates a user and builds the sign-in principal.</summary>
    public Task<IdentityResult<ClaimsPrincipal>> LoginAsync(
        LoginRequest      request,
        ClaimsPrincipal   principal,
        CancellationToken ct = default
    ) => _dispatcher.SendAsync<LoginUserRequest<TUser>, IdentityResult<ClaimsPrincipal>>(
        new(request, principal), ct);

    /// <summary>Refreshes a sign-in principal from an authentication ticket.</summary>
    public Task<IdentityResult<ClaimsPrincipal>> RefreshAsync(
        AuthenticationTicket? ticket,
        ClaimsPrincipal       principal,
        CancellationToken     ct = default
    ) => _dispatcher.SendAsync<RefreshUserRequest<TUser>, IdentityResult<ClaimsPrincipal>>(
        new(ticket, principal), ct);

    /// <summary>Builds the authenticated user's profile claims.</summary>
    public Task<IdentityResult<ClaimsStore>> ProfileAsync(
        ClaimsPrincipal   principal,
        CancellationToken ct = default
    ) => _dispatcher.SendAsync<GetUserProfileQuery<TUser>, IdentityResult<ClaimsStore>>(
        new(principal), ct);

    /// <summary>Sends an email-change confirmation code for the authenticated user.</summary>
    public Task<IdentityResult<Unit>> ChangeEmailAsync(
        ProfileRequest    request,
        ClaimsPrincipal   principal,
        CancellationToken ct = default
    ) => _dispatcher.SendAsync<ChangeUserEmailRequest<TUser>, IdentityResult<Unit>>(
        new(request, principal), ct);

    /// <summary>Sends a phone-change confirmation code for the authenticated user.</summary>
    public Task<IdentityResult<Unit>> ChangePhoneAsync(
        ProfileRequest    request,
        ClaimsPrincipal   principal,
        CancellationToken ct = default
    ) => _dispatcher.SendAsync<ChangeUserPhoneRequest<TUser>, IdentityResult<Unit>>(
        new(request, principal), ct);

    /// <summary>Changes the authenticated user's password.</summary>
    public Task<IdentityResult<Unit>> ChangePasswordAsync(
        ProfileRequest    request,
        ClaimsPrincipal   principal,
        CancellationToken ct = default
    ) => _dispatcher.SendAsync<ChangeUserPasswordRequest<TUser>, IdentityResult<Unit>>(
        new(request, principal), ct);

    /// <summary>Sends a password-reset code to a confirmed contact address.</summary>
    public Task<IdentityResult<Unit>> ForgotAsync(
        ForgetRequest     request,
        ClaimsPrincipal   principal,
        CancellationToken ct = default
    ) => _dispatcher.SendAsync<ForgotUserPasswordRequest<TUser>, IdentityResult<Unit>>(
        new(request, principal), ct);

    /// <summary>Resets a password with a password-reset code.</summary>
    public Task<IdentityResult<Unit>> ResetAsync(
        ResetRequest      request,
        ClaimsPrincipal   principal,
        CancellationToken ct = default
    ) => _dispatcher.SendAsync<ResetUserPasswordRequest<TUser>, IdentityResult<Unit>>(
        new(request, principal), ct);

    /// <summary>Confirms an email address or phone number with a confirmation code.</summary>
    public Task<IdentityResult<Unit>> ConfirmAsync(
        ConfirmRequest    request,
        ClaimsPrincipal   principal,
        CancellationToken ct = default
    ) => _dispatcher.SendAsync<ConfirmUserRequest<TUser>, IdentityResult<Unit>>(
        new(request, principal), ct);

    /// <summary>Sends an account-confirmation code to a contact address.</summary>
    public Task<IdentityResult<Unit>> CodeAsync(
        ForgetRequest     request,
        ClaimsPrincipal   principal,
        CancellationToken ct = default
    ) => _dispatcher.SendAsync<SendUserConfirmationCodeRequest<TUser>, IdentityResult<Unit>>(
        new(request, principal), ct);

    /// <summary>Builds two-factor authenticator enrollment state for the authenticated user.</summary>
    public Task<IdentityResult<AuthenticatorResponse>> AuthenticatorAsync(
        ClaimsPrincipal   principal,
        CancellationToken ct = default
    ) => _dispatcher.SendAsync<GetUserAuthenticatorRequest<TUser>, IdentityResult<AuthenticatorResponse>>(
        new(principal), ct);

    /// <summary>Enables two-factor authenticator sign-in for the authenticated user.</summary>
    public Task<IdentityResult<Unit>> EnrollAsync(
        AuthenticatorRequest request,
        ClaimsPrincipal      principal,
        CancellationToken    ct = default
    ) => _dispatcher.SendAsync<EnrollUserAuthenticatorRequest<TUser>, IdentityResult<Unit>>(
        new(request, principal), ct);

    /// <summary>Disables two-factor authenticator sign-in for the authenticated user.</summary>
    public Task<IdentityResult<Unit>> DowngradeAsync(
        AuthenticatorRequest request,
        ClaimsPrincipal      principal,
        CancellationToken    ct = default
    ) => _dispatcher.SendAsync<DowngradeUserAuthenticatorRequest<TUser>, IdentityResult<Unit>>(
        new(request, principal), ct);
}
