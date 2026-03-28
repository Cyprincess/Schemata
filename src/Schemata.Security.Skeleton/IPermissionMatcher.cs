using System.Security.Claims;

namespace Schemata.Security.Skeleton;

public interface IPermissionMatcher
{
    bool IsMatch(ClaimsPrincipal principal, string permission);
}
