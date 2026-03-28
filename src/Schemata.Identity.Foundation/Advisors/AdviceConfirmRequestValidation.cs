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

public sealed class AdviceConfirmRequestValidation : IIdentityRequestAdvisor<ConfirmRequest>
{
    public const int DefaultOrder = AdviceIdentityFeatureGate.DefaultOrder + 10_000_000;

    #region IIdentityRequestAdvisor<ConfirmRequest> Members

    public int Order => DefaultOrder;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        ConfirmRequest    request,
        IdentityOperation op,
        ClaimsPrincipal   principal,
        CancellationToken ct = default
    ) {
        if (op is not IdentityOperation.Confirm) {
            return Task.FromResult(AdviseResult.Continue);
        }

        if (string.IsNullOrWhiteSpace(request.Code)) {
            throw new ValidationException([new() {
                Field       = nameof(request.Code).Underscore(),
                Description = string.Format(SchemataResources.GetResourceString(SchemataResources.ST1013), nameof(request.Code).Humanize(LetterCasing.Title)),
                Reason      = FieldReasons.NotEmpty,
            }]);
        }

        if (string.IsNullOrWhiteSpace(request.EmailAddress) && string.IsNullOrWhiteSpace(request.PhoneNumber)) {
            throw new ValidationException([
                new() {
                    Field       = nameof(request.EmailAddress).Underscore(),
                    Description = string.Format(SchemataResources.GetResourceString(SchemataResources.ST1013), nameof(request.EmailAddress).Humanize(LetterCasing.Title)),
                    Reason      = FieldReasons.NotEmpty,
                },
                new() {
                    Field       = nameof(request.PhoneNumber).Underscore(),
                    Description = string.Format(SchemataResources.GetResourceString(SchemataResources.ST1013), nameof(request.PhoneNumber).Humanize(LetterCasing.Title)),
                    Reason      = FieldReasons.NotEmpty,
                },
            ]);
        }

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
