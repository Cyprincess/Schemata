using System;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;
using Schemata.Advice;
using Schemata.Identity.Skeleton;
using Schemata.Identity.Skeleton.Advisors;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Models;

namespace Schemata.Identity.Foundation.Handlers;

internal sealed partial class IdentityOperationHandler<TUser>
    where TUser : SchemataUser, new()
{
    /// <summary>
    ///     Normalizes an ASP.NET Identity error code (PascalCase, e.g.
    ///     <c>"PasswordTooShort"</c>) into the AIP-193 UPPER_SNAKE_CASE form
    ///     (<c>"PASSWORD_TOO_SHORT"</c>) required by <see cref="ErrorFieldViolation.Reason" />.
    /// </summary>
    private static string NormalizeIdentityCode(string? code) {
        if (string.IsNullOrEmpty(code)) {
            return "IDENTITY_VALIDATION_FAILED";
        }

        var builder = new StringBuilder(code.Length + 8);
        for (var i = 0; i < code.Length; i++) {
            var c = code[i];
            if (i > 0 && char.IsUpper(c) && (char.IsLower(code[i - 1]) || char.IsDigit(code[i - 1]))) {
                builder.Append('_');
            }

            builder.Append(char.ToUpperInvariant(c));
        }

        return builder.ToString();
    }

    /// <summary>
    ///     Translates a rejected ASP.NET Identity mutation into the shared validation error
    ///     shape, so a failed store/user-validator write surfaces instead of reporting the
    ///     requested account state.
    /// </summary>
    private static void RequireSucceeded(Microsoft.AspNetCore.Identity.IdentityResult result) {
        if (!result.Succeeded) {
            throw new ValidationException(result.Errors.Select(e => new ErrorFieldViolation {
                Reason      = NormalizeIdentityCode(e.Code),
                Description = e.Description,
            }));
        }
    }

    /// <summary>Registers a user and builds the sign-in principal.</summary>
    public async Task<IdentityResult<ClaimsPrincipal>> RegisterAsync(
        RegisterRequest   request,
        ClaimsPrincipal   principal,
        AdviceContext     context,
        CancellationToken ct = default
    ) {

        var execution = new IdentityOperationExecution<IdentityResult<ClaimsPrincipal>> {
            Operation = IdentityOperation.Register,
            Principal = principal,
        };

        var handled = await RunStageAsync(
            execution,
            () => Advisor.For<IIdentityRequestAdvisor<RegisterRequest, IdentityResult<ClaimsPrincipal>>>()
                         .RunAsync(context, request, execution, ct));
        if (handled is not null) {
            return handled;
        }

        var user = new TUser {
            UserName = request.Username,
            Email = request.EmailAddress,
            PhoneNumber = request.PhoneNumber,
        };

        handled = await RunStageAsync(
            execution,
            () => Advisor.For<IIdentityRegisterUserAdvisor>()
                         .RunAsync(context, user, request, execution, ct));
        if (handled is not null) {
            return handled;
        }

        var result = await _users.CreateAsync(user, request.Password);
        if (!result.Succeeded) {
            throw new ValidationException(result.Errors.Select(e => new ErrorFieldViolation {
                Reason      = NormalizeIdentityCode(e.Code),
                Description = e.Description,
            }));
        }

        handled = await RunStageAsync(
            execution,
            () => Advisor.For<IIdentityRegisterAdvisor<TUser>>()
                         .RunAsync(context, user, request, execution, ct));
        if (handled is not null) {
            return handled;
        }

        if (_users.Options.SignIn.RequireConfirmedAccount) {
            await SendConfirmationCodeAsync(user, request.EmailAddress, request.PhoneNumber);
        }

        var claims = await _sign.CreateUserPrincipalAsync(user);
        AuthenticationClaims.Stamp(claims, null, false, _time);

        return IdentityResult<ClaimsPrincipal>.Success(claims);
    }

    /// <summary>Refreshes a sign-in principal from an authentication ticket.</summary>
    public async Task<IdentityResult<ClaimsPrincipal>> RefreshAsync(
        AuthenticationTicket? ticket,
        ClaimsPrincipal       principal,
        AdviceContext         context,
        CancellationToken     ct = default
    ) {
        var execution = new IdentityOperationExecution<IdentityResult<ClaimsPrincipal>> {
            Operation = IdentityOperation.Refresh,
            Principal = principal,
        };

        var handled = await RunStageAsync(
            execution,
            () => Advisor.For<IIdentityRequestAdvisor<Unit, IdentityResult<ClaimsPrincipal>>>()
                         .RunAsync(context, Unit.Value, execution, ct));
        if (handled is not null) {
            return handled;
        }

        // Ticket unprotection authenticates the payload; refresh admission enforces its expiry.
        if (ticket?.Principal is null
         || ticket.Properties?.ExpiresUtc is not { } expiresUtc
         || _time.GetUtcNow() >= expiresUtc
         || await _sign.ValidateSecurityStampAsync(ticket.Principal) is not { } found) {
            return IdentityResult<ClaimsPrincipal>.Challenge();
        }

        handled = await RunStageAsync(
            execution,
            () => Advisor.For<IIdentityRefreshUserAdvisor<TUser>>()
                         .RunAsync(context, found, execution, ct));
        if (handled is not null) {
            return handled;
        }

        var claims = await _sign.CreateUserPrincipalAsync(found);
        AuthenticationClaims.Carry(ticket.Principal, claims);

        // Ticket-bound session identity survives the rebuild through the observers; renewal
        // never mints a fresh session.
        foreach (var observer in _hostObservers) {
            await observer.OnRenewingAsync(claims, ticket.Principal, ct);
        }

        handled = await RunStageAsync(
            execution,
            () => Advisor.For<IIdentityRefreshAdvisor>()
                         .RunAsync(context, claims, execution, ct));
        if (handled is not null) {
            return handled;
        }

        return IdentityResult<ClaimsPrincipal>.Success(claims);
    }
}
