using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Identity.Foundation.Commands;
using Schemata.Identity.Skeleton;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Messaging.Skeleton;

namespace Schemata.Identity.Foundation.Handlers;

internal sealed class SendUserConfirmationCodeHandler<TUser>(IdentityOperationHandler<TUser> operations)
    : IRequestHandler<SendUserConfirmationCodeRequest<TUser>, IdentityResult<Unit>>
    where TUser : SchemataUser, new()
{
    public Task<IdentityResult<Unit>> HandleAsync(
        SendUserConfirmationCodeRequest<TUser> request,
        CancellationToken                      ct = default
    ) => operations.CodeAsync(IdentityRequestHandler.Require(request).Request, request.Principal!, ct);
}