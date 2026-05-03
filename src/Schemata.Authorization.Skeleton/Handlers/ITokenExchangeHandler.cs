using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Models;

namespace Schemata.Authorization.Skeleton.Handlers;

/// <summary>
///     Handles token exchange for a specific subject token type at the token endpoint.
///     Each implementation is identified by its <see cref="SubjectTokenType" /> URI,
///     per <seealso href="https://www.rfc-editor.org/rfc/rfc8693.html">RFC 8693: OAuth 2.0 Token Exchange</seealso>.
/// </summary>
public interface ITokenExchangeHandler<TApplication>
    where TApplication : SchemataApplication
{
    /// <summary>URI identifying the subject token type this handler accepts.</summary>
    string SubjectTokenType { get; }

    /// <summary>Processes a token exchange request for this subject token type.</summary>
    Task<AuthorizationResult> HandleAsync(
        TApplication      application,
        TokenRequest      request,
        ClaimsPrincipal?  principal,
        CancellationToken ct
    );
}
