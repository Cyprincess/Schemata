using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Humanizer;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Identity.Skeleton;
using Schemata.Identity.Skeleton.Advisors;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Managers;
using Schemata.Identity.Skeleton.Models;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Identity.Foundation.Advisors;

public static class AdviceChangePasswordValidation
{
    public const int DefaultOrder = AdviceChangePhoneValidation.DefaultOrder + 10_000_000;
}

public sealed class AdviceChangePasswordValidation<TUser>(SchemataUserManager<TUser> users) : IIdentityRequestAdvisor<ProfileRequest>
    where TUser : SchemataUser, new()
{
    #region IIdentityRequestAdvisor<ProfileRequest> Members

    public int Order => AdviceChangePasswordValidation.DefaultOrder;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        ProfileRequest    request,
        IdentityOperation op,
        ClaimsPrincipal   principal,
        CancellationToken ct = default
    ) {
        if (op is not IdentityOperation.ChangePassword) {
            return AdviseResult.Continue;
        }

        if (await users.GetUserAsync(principal) is null) {
            throw new NotFoundException();
        }

        if (string.IsNullOrWhiteSpace(request.OldPassword)) {
            throw new ValidationException([new() {
                Field       = nameof(request.OldPassword).Underscore(),
                Description = string.Format(SchemataResources.GetResourceString(SchemataResources.ST1013), nameof(request.OldPassword).Humanize(LetterCasing.Title)),
                Reason      = FieldReasons.NotEmpty,
            }]);
        }

        if (string.IsNullOrWhiteSpace(request.NewPassword)) {
            throw new ValidationException([new() {
                Field       = nameof(request.NewPassword).Underscore(),
                Description = string.Format(SchemataResources.GetResourceString(SchemataResources.ST1013), nameof(request.NewPassword).Humanize(LetterCasing.Title)),
                Reason      = FieldReasons.NotEmpty,
            }]);
        }

        if (string.Equals(request.NewPassword, request.OldPassword)) {
            throw new NoContentException();
        }

        return AdviseResult.Continue;
    }

    #endregion
}
