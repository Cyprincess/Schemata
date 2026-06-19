using System.Security.Claims;

namespace Schemata.Security.Skeleton;

/// <summary>Matches resolved permission names against a principal.</summary>
public interface IPermissionMatcher
{
    /// <summary>Returns whether the principal has a permission.</summary>
    /// <param name="principal">Principal containing permission claims.</param>
    /// <param name="permission">Permission name to match.</param>
    /// <returns><see langword="true"/> when the permission is present; otherwise, <see langword="false"/>.</returns>
    bool IsMatch(ClaimsPrincipal principal, string permission);
}
