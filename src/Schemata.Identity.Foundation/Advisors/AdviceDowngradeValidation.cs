using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Humanizer;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Identity.Skeleton;
using Schemata.Identity.Skeleton.Advisors;
using Schemata.Identity.Skeleton.Models;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Identity.Foundation.Advisors;

public sealed class AdviceDowngradeValidation : IIdentityRequestAdvisor<AuthenticatorRequest>
{
    public const int DefaultOrder = AdviceEnrollValidation.DefaultOrder + 10_000_000;

    #region IIdentityRequestAdvisor<AuthenticatorRequest> Members

    public int Order => DefaultOrder;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext        ctx,
        AuthenticatorRequest request,
        IdentityOperation    op,
        ClaimsPrincipal      principal,
        CancellationToken    ct = default
    ) {
        if (op is not IdentityOperation.Downgrade) {
            return Task.FromResult(AdviseResult.Continue);
        }

        if (string.IsNullOrWhiteSpace(request.TwoFactorCode)
         && string.IsNullOrWhiteSpace(request.TwoFactorRecoveryCode)) {
            throw new ValidationException([
                new() {
                    Field       = nameof(request.TwoFactorCode).Underscore(),
                    Description = string.Format(SchemataResources.GetResourceString(SchemataResources.ST1013), nameof(request.TwoFactorCode).Humanize(LetterCasing.Title)),
                    Reason      = FieldReasons.NotEmpty,
                },
                new() {
                    Field       = nameof(request.TwoFactorRecoveryCode).Underscore(),
                    Description = string.Format(SchemataResources.GetResourceString(SchemataResources.ST1013), nameof(request.TwoFactorRecoveryCode).Humanize(LetterCasing.Title)),
                    Reason      = FieldReasons.NotEmpty,
                },
            ]);
        }

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
