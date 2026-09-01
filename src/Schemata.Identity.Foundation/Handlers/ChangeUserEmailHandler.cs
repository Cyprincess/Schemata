using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Identity.Foundation.Commands;
using Schemata.Identity.Skeleton;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Messaging.Skeleton;

namespace Schemata.Identity.Foundation.Handlers;

internal sealed class ChangeUserEmailHandler<TUser>(IdentityOperationHandler<TUser> operations)
    : IRequestHandler<ChangeUserEmailRequest<TUser>, IdentityResult<Unit>>
    where TUser : SchemataUser, new()
{
    public Task<IdentityResult<Unit>> HandleAsync(
        ChangeUserEmailRequest<TUser> request,
        CancellationToken             ct = default
    ) => operations.ChangeEmailAsync(IdentityRequestHandler.Require(request).Request, request.Principal!, ct);
}