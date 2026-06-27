using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using System.Threading.Tasks;
using Humanizer;
using Schemata.Abstractions;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;
using Schemata.Common.Errors;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Managers;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Identity.Foundation.Advisors;

/// <summary>
///     Provides shared required-field violations and authenticated-user lookup for identity request-validation advisors.
/// </summary>
internal static class IdentityValidation
{
    /// <summary>Builds a <see cref="SchemataResources.NOT_EMPTY" /> violation for the CLR property <paramref name="field" />.</summary>
    /// <param name="field">The CLR property name (rendered as snake_case field and a Title-cased label).</param>
    /// <returns>The field violation.</returns>
    public static ErrorFieldViolation NotEmptyError(string field) {
        return new() {
            Field       = field.Underscore(),
            Description = string.Format(SchemataResources.GetResourceString(SchemataResources.NOT_EMPTY), field.Humanize(LetterCasing.Title)),
            Reason      = SchemataResources.NOT_EMPTY,
        };
    }

    /// <summary>Throws a <see cref="ValidationException" /> when <paramref name="value" /> is blank.</summary>
    /// <param name="value">The value to check.</param>
    /// <param name="field">The CLR property name reported in the violation.</param>
    public static void RequireNotEmpty([NotNull] string? value, string field) {
        if (string.IsNullOrWhiteSpace(value)) {
            throw new ValidationException([NotEmptyError(field)]);
        }
    }

    /// <summary>Resolves the authenticated user or throws a resource-themed <see cref="NotFoundException" />.</summary>
    /// <typeparam name="TUser">The user entity type.</typeparam>
    /// <param name="users">The user manager.</param>
    /// <param name="principal">The current principal.</param>
    /// <returns>The resolved user.</returns>
    public static async Task<TUser> RequireUserAsync<TUser>(SchemataUserManager<TUser> users, ClaimsPrincipal principal)
        where TUser : SchemataUser, new() {
        return await users.GetUserAsync(principal) is { } user
            ? user
            : throw SchemataResourceErrors.NotFound<TUser>(principal.FindFirstValue(Claims.Subject));
    }
}
