using System.Collections.Generic;

namespace Schemata.Authorization.Skeleton.Models;

public class AuthorizeResponse
{
    public virtual string? ApplicationName { get; set; }

    public virtual List<ScopeResponse>? Scopes { get; set; }
}
