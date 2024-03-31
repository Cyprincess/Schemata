using System.Collections.Generic;

namespace Schemata.Authorization.Foundation.Models;

public class AuthorizeResponse
{
    public string? ApplicationName { get; set; }

    public List<ScopeResponse>? Scopes { get; set; }
}
