using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Identity.Skeleton;
using Schemata.Identity.Skeleton.Advisors;
using Schemata.Identity.Skeleton.Models;

namespace Schemata.Identity.Foundation.Advisors;

/// <summary>Validates two-factor downgrade requests.</summary>
public sealed class AdviceRequestDowngradeValidation : IIdentityRequestAdvisor<AuthenticatorRequest, IdentityResult<Unit>>
{
    /// <summary>Default order for two-factor downgrade request validation.</summary>
    public const int DefaultOrder = AdviceRequestEnrollValidation.DefaultOrder + 10_000_000;

    #region IIdentityRequestAdvisor<AuthenticatorRequest, IdentityResult<Unit>> Members

    public int Order => DefaultOrder;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext                                    ctx,
        AuthenticatorRequest                             request,
        IdentityOperationExecution<IdentityResult<Unit>> execution,
        CancellationToken                                ct = default
    ) {
        if (execution.Operation is not IdentityOperation.Downgrade) {
            return Task.FromResult(AdviseResult.Continue);
        }

        if (string.IsNullOrWhiteSpace(request.TwoFactorCode)
         && string.IsNullOrWhiteSpace(request.TwoFactorRecoveryCode)) {
            throw new ValidationException([
                IdentityValidation.NotEmptyError(nameof(request.TwoFactorCode)),
                IdentityValidation.NotEmptyError(nameof(request.TwoFactorRecoveryCode)),
            ]);
        }

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
