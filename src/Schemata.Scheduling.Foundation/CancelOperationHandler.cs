using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Resource;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;

namespace Schemata.Scheduling.Foundation;

/// <summary>
///     AIP-136 <c>:cancel</c> handler on <see cref="SchemataJobExecution" />.
///     Delegates cancellation semantics to <see cref="IOperationService" />.
/// </summary>
public sealed class CancelOperationHandler(IOperationService operations)
    : IResourceMethodHandler<SchemataJobExecution, EmptyResourceRequest, Operation>
{
    #region IResourceMethodHandler<SchemataJobExecution, EmptyResourceRequest, Operation> Members

    public async ValueTask<Operation> InvokeAsync(
        string?               name,
        EmptyResourceRequest  request,
        SchemataJobExecution? entity,
        ClaimsPrincipal?      principal,
        CancellationToken     ct
    ) {
        ArgumentNullException.ThrowIfNull(entity);

        return await operations.CancelAsync(entity.CanonicalName ?? $"operations/{entity.Uid:n}", ct);
    }

    #endregion
}
