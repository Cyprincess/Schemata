using System;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;
using Schemata.Identity.Skeleton.Models;
using Schemata.Identity.Skeleton.Services;

namespace Schemata.Identity.Foundation.Services;

/// <summary>Applies one configured alias selection policy to registration and login requests.</summary>
public sealed class ConfiguredUserAliasResolver(UserNameAliasMode mode, IPhoneNumberNormalizer phoneNumbers) : IUserAliasResolver
{
    public UserAliasSelection ResolveRegistration(RegisterRequest request) {
        var phone = AssemblePhone(request.PhoneNumber, request.PhoneRegion);
        request.PhoneNumber = phone;
        var selected = Select(mode, request.Username, request.EmailAddress, phone, request.UsernameType, false);
        if (selected.Type != UserAliasType.PhoneNumber) return selected;
        return selected with { RegistrationName = CanonicalPhone(phone) };
    }

    public UserAliasSelection ResolveLogin(LoginRequest request) {
        var phone = AssemblePhone(request.PhoneNumber, request.PhoneRegion);
        return Select(mode, request.Username, request.EmailAddress, phone, request.UsernameType, true);
    }

    private string CanonicalPhone(string? phone) {
        if (phone is null) throw Invalid("phone_number", "The phone_number field is required by the selected alias mode.");
        try {
            return phoneNumbers.NormalizePhoneNumber(phone);
        } catch (FormatException) {
            throw Invalid("phone_number", "The phone number must be a complete E.164 value with at most 15 digits.");
        }
    }

    private static UserAliasSelection Select(
        UserNameAliasMode mode,
        string? username,
        string? email,
        string? phone,
        string? type,
        bool login
    ) {
        if (mode != UserNameAliasMode.Explicit && !string.IsNullOrWhiteSpace(type)) {
            throw Invalid("username_type", "The username_type field requires Explicit alias mode.");
        }

        switch (mode) {
            case UserNameAliasMode.Disabled:
            {
                var name = Required(username, "username");
                if (login && (!string.IsNullOrWhiteSpace(email) || !string.IsNullOrWhiteSpace(phone))) {
                    throw Invalid("username", "Disabled alias mode accepts only the username login key.");
                }
                return new(UserAliasType.UserName, name, name);
            }
            case UserNameAliasMode.Enabled:
            {
                if (!string.IsNullOrWhiteSpace(username)) return new(UserAliasType.UserName, username, username);
                var hasEmail = !string.IsNullOrWhiteSpace(email);
                var hasPhone = !string.IsNullOrWhiteSpace(phone);
                if (hasEmail == hasPhone) {
                    throw Invalid("username", hasEmail
                        ? "Email and phone aliases are ambiguous without an explicit username_type."
                        : "Supply a username, email address, or phone number.");
                }
                if (hasEmail) {
                    var value = Required(email, "email_address");
                    return new(UserAliasType.Email, value, value);
                }
                var number = Required(phone, "phone_number");
                return new(UserAliasType.PhoneNumber, number, number);
            }
            case UserNameAliasMode.Explicit:
            {
                var selectedType = Required(type, "username_type");
                return selectedType switch {
                    "user-name" => Selection(UserAliasType.UserName, Required(username, "username")),
                    "email" => Selection(UserAliasType.Email, Required(email, "email_address")),
                    "phone-number" => Selection(UserAliasType.PhoneNumber, Required(phone, "phone_number")),
                    _ => throw Invalid("username_type", "The username_type must be user-name, email, or phone-number."),
                };
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
        }
    }

    private static UserAliasSelection Selection(UserAliasType type, string value) => new(type, value, value);

    private static string Required(string? value, string field) {
        if (string.IsNullOrWhiteSpace(value)) throw Invalid(field, $"The {field} field is required by the selected alias mode.");
        return value;
    }

    private static string? AssemblePhone(string? phone, string? region) {
        if (string.IsNullOrWhiteSpace(region)) return phone;
        if (string.IsNullOrWhiteSpace(phone)) throw Invalid("phone_number", "A split phone requires its national number.");
        if (phone.StartsWith('+')) throw Invalid("phone_number", "A complete phone and the split phone form are ambiguous.");
        var prefix = region.StartsWith('+') ? region : $"+{region}";
        return prefix + phone;
    }

    private static ValidationException Invalid(string field, string description) {
        return new([new ErrorFieldViolation { Field = field, Reason = "INVALID_ALIAS", Description = description }]);
    }
}
