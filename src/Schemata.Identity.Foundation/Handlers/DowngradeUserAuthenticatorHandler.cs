using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Identity.Foundation.Commands;
using Schemata.Identity.Skeleton;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Messaging.Skeleton;

namespace Schemata.Identity.Foundation.Handlers;

internal sealed class DowngradeUserAuthenticatorHandler<TUser>(IdentityOperationHandler<TUser> operations)
    : IRequestHandler<DowngradeUserAuthenticatorRequest<TUser>, IdentityResult<Unit>>
    where TUser : SchemataUser, new()
{
    public Task<IdentityResult<Unit>> HandleAsync(
        DowngradeUserAuthenticatorRequest<TUser> request,
        CancellationToken                        ct = default
    ) => operations.DowngradeAsync(IdentityRequestHandler.Require(request).Request, request.Principal!, ct);
}