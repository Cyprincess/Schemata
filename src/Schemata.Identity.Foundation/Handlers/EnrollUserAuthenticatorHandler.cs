using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Identity.Foundation.Commands;
using Schemata.Identity.Skeleton;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Messaging.Skeleton;

namespace Schemata.Identity.Foundation.Handlers;

internal sealed class EnrollUserAuthenticatorHandler<TUser>(IdentityOperationHandler<TUser> operations)
    : IRequestHandler<EnrollUserAuthenticatorRequest<TUser>, IdentityResult<Unit>>
    where TUser : SchemataUser, new()
{
    public Task<IdentityResult<Unit>> HandleAsync(
        EnrollUserAuthenticatorRequest<TUser> request,
        CancellationToken                     ct = default
    ) => operations.EnrollAsync(IdentityRequestHandler.Require(request).Request, request.Principal!, ct);
}