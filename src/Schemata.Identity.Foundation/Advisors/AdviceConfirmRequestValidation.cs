using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Identity.Skeleton;
using Schemata.Identity.Skeleton.Advisors;
using Schemata.Identity.Skeleton.Models;

namespace Schemata.Identity.Foundation.Advisors;

/// <summary>Validates account-confirmation requests.</summary>
public sealed class AdviceConfirmRequestValidation : IIdentityRequestAdvisor<ConfirmRequest>
{
    /// <summary>Default order for account-confirmation request validation.</summary>
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

        IdentityValidation.RequireNotEmpty(request.Code, nameof(request.Code));

        if (string.IsNullOrWhiteSpace(request.EmailAddress) && string.IsNullOrWhiteSpace(request.PhoneNumber)) {
            throw new ValidationException([
                IdentityValidation.NotEmptyError(nameof(request.EmailAddress)),
                IdentityValidation.NotEmptyError(nameof(request.PhoneNumber)),
            ]);
        }

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
