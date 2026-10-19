using System.Collections.Generic;
using Microsoft.AspNetCore.Identity;
using Schemata.Abstractions;
using Schemata.Abstractions.Errors;

namespace Schemata.Identity.Foundation.Services;

/// <summary>
///     Localizes <see cref="IdentityError" /> descriptions through
///     <see cref="SchemataResources" /> so <c>IdentityResult</c> failures surface in the
///     caller's locale; the <c>Code</c> values are unchanged, keeping the
///     UPPER_SNAKE_CASE <c>ErrorFieldViolation.Reason</c> mapping intact.
/// </summary>
public sealed class SchemataErrorDescriber : IdentityErrorDescriber
{
    public override IdentityError DefaultError() {
        return Err(base.DefaultError(), SchemataResources.DEFAULT_ERROR);
    }

    public override IdentityError ConcurrencyFailure() {
        return Err(base.ConcurrencyFailure(), SchemataResources.CONCURRENCY_FAILURE);
    }




    public override IdentityError InvalidUserName(string? userName) {
        return Err(base.InvalidUserName(userName), SchemataResources.INVALID_USER_NAME, new() { ["userName"] = userName });
    }

    public override IdentityError InvalidEmail(string? email) {
        return Err(base.InvalidEmail(email), SchemataResources.INVALID_EMAIL, new() { ["email"] = email });
    }

    public override IdentityError DuplicateUserName(string userName) {
        return Err(base.DuplicateUserName(userName), SchemataResources.DUPLICATE_USER_NAME, new() { ["userName"] = userName });
    }

    public override IdentityError DuplicateEmail(string email) {
        return Err(base.DuplicateEmail(email), SchemataResources.DUPLICATE_EMAIL, new() { ["email"] = email });
    }

    public override IdentityError InvalidRoleName(string? role) {
        return Err(base.InvalidRoleName(role), SchemataResources.INVALID_ROLE_NAME, new() { ["role"] = role });
    }

    public override IdentityError DuplicateRoleName(string role) {
        return Err(base.DuplicateRoleName(role), SchemataResources.DUPLICATE_ROLE_NAME, new() { ["role"] = role });
    }

    public override IdentityError UserAlreadyInRole(string role) {
        return Err(base.UserAlreadyInRole(role), SchemataResources.USER_ALREADY_IN_ROLE, new() { ["role"] = role });
    }

    public override IdentityError UserNotInRole(string role) {
        return Err(base.UserNotInRole(role), SchemataResources.USER_NOT_IN_ROLE, new() { ["role"] = role });
    }

    public override IdentityError PasswordTooShort(int length) {
        return Err(base.PasswordTooShort(length), SchemataResources.PASSWORD_TOO_SHORT, new() { ["length"] = length.ToString() });
    }

    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars) {
        return Err(base.PasswordRequiresUniqueChars(uniqueChars), SchemataResources.PASSWORD_REQUIRES_UNIQUE_CHARS, new() {
            ["uniqueChars"] = uniqueChars.ToString(),
        });
    }

    public override IdentityError PasswordRequiresNonAlphanumeric() {
        return Err(base.PasswordRequiresNonAlphanumeric(), SchemataResources.PASSWORD_REQUIRES_NON_ALPHANUMERIC);
    }

    public override IdentityError PasswordRequiresDigit() {
        return Err(base.PasswordRequiresDigit(), SchemataResources.PASSWORD_REQUIRES_DIGIT);
    }

    public override IdentityError PasswordRequiresLower() {
        return Err(base.PasswordRequiresLower(), SchemataResources.PASSWORD_REQUIRES_LOWER);
    }

    public override IdentityError PasswordRequiresUpper() {
        return Err(base.PasswordRequiresUpper(), SchemataResources.PASSWORD_REQUIRES_UPPER);
    }

    public override IdentityError InvalidToken() {
        return Err(base.InvalidToken(), SchemataResources.INVALID_TOKEN);
    }

    public override IdentityError RecoveryCodeRedemptionFailed() {
        return Err(base.RecoveryCodeRedemptionFailed(), SchemataResources.RECOVERY_CODE_REDEMPTION_FAILED);
    }

    public override IdentityError LoginAlreadyAssociated() {
        return Err(base.LoginAlreadyAssociated(), SchemataResources.LOGIN_ALREADY_ASSOCIATED);
    }

    public override IdentityError UserAlreadyHasPassword() {
        return Err(base.UserAlreadyHasPassword(), SchemataResources.USER_ALREADY_HAS_PASSWORD);
    }

    public override IdentityError PasswordMismatch() {
        return Err(base.PasswordMismatch(), SchemataResources.PASSWORD_MISMATCH);
    }


    private static IdentityError Err(IdentityError error, string key, Dictionary<string, string?>? args = null) {
        var description = args is { Count: > 0 }
            ? LocalizedMessageFormatter.FormatLocalized(key, args)
            : SchemataResources.GetResourceString(key);

        error.Description = description ?? key;

        return error;
    }
}
