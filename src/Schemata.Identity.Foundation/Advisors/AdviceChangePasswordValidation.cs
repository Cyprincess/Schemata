using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Identity.Skeleton;
using Schemata.Identity.Skeleton.Advisors;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Managers;
using Schemata.Identity.Skeleton.Models;

namespace Schemata.Identity.Foundation.Advisors;

/// <summary>Provides the order for password-change request validation.</summary>
public static class AdviceChangePasswordValidation
{
    /// <summary>Default order for password-change request validation.</summary>
    public const int DefaultOrder = AdviceChangePhoneValidation.DefaultOrder + 10_000_000;
}

/// <summary>Validates password-change profile requests.</summary>
/// <typeparam name="TUser">User entity type handled by the user manager.</typeparam>
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

        await IdentityValidation.RequireUserAsync(users, principal);

        IdentityValidation.RequireNotEmpty(request.OldPassword, nameof(request.OldPassword));
        IdentityValidation.RequireNotEmpty(request.NewPassword, nameof(request.NewPassword));

        if (string.Equals(request.NewPassword, request.OldPassword)) {
            throw new NoContentException();
        }

        return AdviseResult.Continue;
    }

    #endregion
}
