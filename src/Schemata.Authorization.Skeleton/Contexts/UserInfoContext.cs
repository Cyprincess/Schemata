using System.Collections.Generic;
using System.Security.Claims;

namespace Schemata.Authorization.Skeleton.Contexts;

public sealed class UserInfoContext
{
    public ClaimsPrincipal? Principal       { get; set; }
    public string?          InternalSubject { get; set; }
    public HashSet<string>  GrantedScopes   { get; set; } = new();
    public bool             IsEndUserToken  { get; set; }
}
