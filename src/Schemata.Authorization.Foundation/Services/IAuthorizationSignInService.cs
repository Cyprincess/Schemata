using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Authorization.Foundation.Services;

/// <summary>Issues protocol responses from an authorized principal without writing HTTP state.</summary>
public interface IAuthorizationSignInService
{
    Task<AuthorizationSignInResponse> IssueAsync(
        ClaimsPrincipal                 principal,
        IDictionary<string, string?>?   properties,
        AuthorizationSignInResponseKind kind,
        CancellationToken               ct = default);
}