using System.Collections.Generic;

namespace Schemata.Authorization.Foundation.Models;

public class VerifyResponse
{
    public string? ApplicationName { get; set; }

    public string? UserCode { get; set; }

    public List<ScopeResponse>? Scopes { get; set; }
}
