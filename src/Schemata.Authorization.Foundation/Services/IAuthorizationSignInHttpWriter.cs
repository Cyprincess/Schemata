using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Schemata.Authorization.Foundation.Services;

/// <summary>Writes an issued authorization response for compatibility authentication-scheme calls.</summary>
public interface IAuthorizationSignInHttpWriter
{
    Task WriteAsync(
        HttpContext                 context,
        AuthorizationSignInResponse response,
        CancellationToken           ct = default);
}