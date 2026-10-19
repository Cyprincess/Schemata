using System;
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

/// <summary>Provides the order for email-change request validation.</summary>
public static class AdviceRequestEmailValidation
{
    /// <summary>Default order for email-change request validation.</summary>
    public const int DefaultOrder = Schemata.Abstractions.SchemataConstants.Orders.Base + 10_000_000;
}

/// <summary>Validates email-change profile requests.</summary>
/// <typeparam name="TUser">User entity type handled by the user manager.</typeparam>
public sealed class AdviceRequestEmailValidation<TUser>(SchemataUserManager<TUser> users) : IIdentityRequestAdvisor<ProfileRequest, IdentityResult<Unit>>
    where TUser : SchemataUser, new()
{
    #region IIdentityRequestAdvisor<ProfileRequest, IdentityResult<Unit>> Members

    public int Order => AdviceRequestEmailValidation.DefaultOrder;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext                                    ctx,
        ProfileRequest                                   request,
        IdentityOperationExecution<IdentityResult<Unit>> execution,
        CancellationToken                                ct = default
    ) {
        if (execution.Operation is not IdentityOperation.ChangeEmail) {
            return AdviseResult.Continue;
        }

        var user = await IdentityValidation.RequireUserAsync(users, execution.Principal);

        IdentityValidation.RequireNotEmpty(request.EmailAddress, nameof(request.EmailAddress));

        if (string.Equals(request.EmailAddress, user.Email, StringComparison.InvariantCultureIgnoreCase)) {
            throw new NoContentException();
        }

        return AdviseResult.Continue;
    }

    #endregion
}
