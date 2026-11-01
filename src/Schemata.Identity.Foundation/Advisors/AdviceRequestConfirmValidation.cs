using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Identity.Skeleton;
using Schemata.Identity.Skeleton.Advisors;
using Schemata.Identity.Skeleton.Models;

namespace Schemata.Identity.Foundation.Advisors;

/// <summary>Validates account-confirmation requests.</summary>
public sealed class AdviceRequestConfirmValidation : IIdentityRequestAdvisor<ConfirmRequest, IdentityResult<Unit>>
{
    /// <summary>Default order for account-confirmation request validation.</summary>
    public const int DefaultOrder = Schemata.Abstractions.SchemataConstants.Orders.Base + 10_000_000;

    #region IIdentityRequestAdvisor<ConfirmRequest, IdentityResult<Unit>> Members

    public int Order => DefaultOrder;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext                                    ctx,
        ConfirmRequest                                   request,
        IdentityOperationExecution<IdentityResult<Unit>> execution,
        CancellationToken                                ct = default
    ) {
        if (execution.Operation is not IdentityOperation.Confirm) {
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
