using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Models;

namespace Schemata.Authorization.Skeleton.Handlers;

public interface ITokenExchangeHandler<TApplication>
    where TApplication : SchemataApplication
{
    string SubjectTokenType { get; }

    Task<AuthorizationResult> HandleAsync(
        TApplication      application,
        TokenRequest      request,
        ClaimsPrincipal?  principal,
        CancellationToken ct
    );
}
