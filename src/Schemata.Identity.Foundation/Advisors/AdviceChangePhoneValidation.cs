using System;
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

public static class AdviceChangePhoneValidation
{
    public const int DefaultOrder = AdviceChangeEmailValidation.DefaultOrder + 10_000_000;
}

public sealed class AdviceChangePhoneValidation<TUser>(SchemataUserManager<TUser> users) : IIdentityRequestAdvisor<ProfileRequest>
    where TUser : SchemataUser, new()
{
    #region IIdentityRequestAdvisor<ProfileRequest> Members

    public int Order => AdviceChangePhoneValidation.DefaultOrder;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        ProfileRequest    request,
        IdentityOperation op,
        ClaimsPrincipal   principal,
        CancellationToken ct = default
    ) {
        if (op is not IdentityOperation.ChangePhone) {
            return AdviseResult.Continue;
        }

        if (await users.GetUserAsync(principal) is not { } user) {
            throw new NotFoundException();
        }

        if (string.IsNullOrWhiteSpace(request.PhoneNumber)) {
            throw new ValidationException([new() {
                Field       = nameof(request.PhoneNumber).Underscore(),
                Description = string.Format(SchemataResources.GetResourceString(SchemataResources.ST1013), nameof(request.PhoneNumber).Humanize(LetterCasing.Title)),
                Reason      = FieldReasons.NotEmpty,
            }]);
        }

        if (string.Equals(request.PhoneNumber, user.PhoneNumber, StringComparison.InvariantCultureIgnoreCase)) {
            throw new NoContentException();
        }

        return AdviseResult.Continue;
    }

    #endregion
}
