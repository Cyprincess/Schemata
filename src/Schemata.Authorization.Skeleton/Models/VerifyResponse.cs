using System.Collections.Generic;

namespace Schemata.Authorization.Skeleton.Models;

public class VerifyResponse
{
    public virtual string? ApplicationName { get; set; }

    public virtual string? UserCode { get; set; }

    public virtual List<ScopeResponse>? Scopes { get; set; }
}
