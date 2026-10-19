using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Identity.Skeleton;
using Schemata.Identity.Skeleton.Advisors;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Managers;
using Schemata.Identity.Skeleton.Models;

namespace Schemata.Identity.Foundation.Advisors;

/// <summary>Provides the order for two-factor enrollment request validation.</summary>
public static class AdviceRequestEnrollValidation
{
    /// <summary>Default order for two-factor enrollment request validation.</summary>
    public const int DefaultOrder = Schemata.Abstractions.SchemataConstants.Orders.Base + 10_000_000;
}

/// <summary>Validates two-factor enrollment requests.</summary>
/// <typeparam name="TUser">User entity type handled by the user manager.</typeparam>
public sealed class AdviceRequestEnrollValidation<TUser>(SchemataUserManager<TUser> users) : IIdentityRequestAdvisor<AuthenticatorRequest, IdentityResult<Unit>>
    where TUser : SchemataUser, new()
{
    #region IIdentityRequestAdvisor<AuthenticatorRequest, IdentityResult<Unit>> Members

    public int Order => AdviceRequestEnrollValidation.DefaultOrder;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext                                    ctx,
        AuthenticatorRequest                             request,
        IdentityOperationExecution<IdentityResult<Unit>> execution,
        CancellationToken                                ct = default
    ) {
        if (execution.Operation is not IdentityOperation.Enroll) {
            return AdviseResult.Continue;
        }

        var user = await IdentityValidation.RequireUserAsync(users, execution.Principal);

        IdentityValidation.RequireNotEmpty(request.TwoFactorCode, nameof(request.TwoFactorCode));

        if (!await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, request.TwoFactorCode)) {
            throw new PermissionDeniedException();
        }

        return AdviseResult.Continue;
    }

    #endregion
}
