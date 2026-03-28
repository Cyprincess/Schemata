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

public static class AdviceEnrollValidation
{
    public const int DefaultOrder = AdviceIdentityFeatureGate.DefaultOrder + 10_000_000;
}

public sealed class AdviceEnrollValidation<TUser>(SchemataUserManager<TUser> users) : IIdentityRequestAdvisor<AuthenticatorRequest>
    where TUser : SchemataUser, new()
{
    #region IIdentityRequestAdvisor<AuthenticatorRequest> Members

    public int Order => AdviceEnrollValidation.DefaultOrder;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext        ctx,
        AuthenticatorRequest request,
        IdentityOperation    op,
        ClaimsPrincipal      principal,
        CancellationToken    ct = default
    ) {
        if (op is not IdentityOperation.Enroll) {
            return AdviseResult.Continue;
        }

        if (await users.GetUserAsync(principal) is not { } user) {
            throw new NotFoundException();
        }

        if (string.IsNullOrWhiteSpace(request.TwoFactorCode)) {
            throw new ValidationException([new() {
                Field       = nameof(request.TwoFactorCode).Underscore(),
                Description = string.Format(SchemataResources.GetResourceString(SchemataResources.ST1013), nameof(request.TwoFactorCode).Humanize(LetterCasing.Title)),
                Reason      = FieldReasons.NotEmpty,
            }]);
        }

        if (!await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, request.TwoFactorCode)) {
            throw new AuthorizationException();
        }

        return AdviseResult.Continue;
    }

    #endregion
}
