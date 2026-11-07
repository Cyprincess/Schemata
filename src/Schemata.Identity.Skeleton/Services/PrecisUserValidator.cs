using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;

namespace Schemata.Identity.Skeleton.Services;

/// <summary>Preserves Identity validation while delegating username character policy to NormalizeName.</summary>
/// <typeparam name="TUser">The user entity type.</typeparam>
public sealed class PrecisUserValidator<TUser> : UserValidator<TUser>
    where TUser : class
{
    public override async Task<IdentityResult> ValidateAsync(UserManager<TUser> manager, TUser user) {
        var result = await base.ValidateAsync(manager, user);
        if (result.Succeeded) {
            return result;
        }

        var userName = await manager.GetUserNameAsync(user);
        if (string.IsNullOrWhiteSpace(userName)) {
            return result;
        }

        try {
            manager.NormalizeName(userName);
        } catch (FormatException) {
            return result;
        }

        var errors = result.Errors
                           .Where(error => error.Code != nameof(IdentityErrorDescriber.InvalidUserName))
                           .ToArray();
        return errors.Length == 0 ? IdentityResult.Success : IdentityResult.Failed(errors);
    }
}
