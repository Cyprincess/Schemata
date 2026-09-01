using System.Threading;
using System.Threading.Tasks;
using Schemata.Identity.Foundation.Commands;
using Schemata.Identity.Skeleton;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Models;
using Schemata.Messaging.Skeleton;

namespace Schemata.Identity.Foundation.Handlers;

internal sealed class GetUserAuthenticatorHandler<TUser>(IdentityOperationHandler<TUser> operations)
    : IRequestHandler<GetUserAuthenticatorRequest<TUser>, IdentityResult<AuthenticatorResponse>>
    where TUser : SchemataUser, new()
{
    public Task<IdentityResult<AuthenticatorResponse>> HandleAsync(
        GetUserAuthenticatorRequest<TUser> request,
        CancellationToken                  ct = default
    ) => operations.AuthenticatorAsync(IdentityRequestHandler.Require(request).Principal!, ct);
}