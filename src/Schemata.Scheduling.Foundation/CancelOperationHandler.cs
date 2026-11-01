using Schemata.Abstractions.Resource;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Messaging.Skeleton;
using Schemata.Scheduling.Foundation.Commands;
using Schemata.Scheduling.Skeleton;
using Schemata.Scheduling.Skeleton.Entities;
using Schemata.Abstractions.Advisors;

namespace Schemata.Scheduling.Foundation;

/// <summary>
///     AIP-136 <c>:cancel</c> handler on <see cref="SchemataJobExecution" />.
///     Delegates cancellation semantics to <see cref="IOperationService" />.
/// </summary>
public sealed class CancelOperationHandler(IOperationService operations)
    : IRequestHandler<CancelOperationRequest, Operation>
{
    public async Task<Operation> HandleAsync(
        CancelOperationRequest request,
        AdviceContext          context,
        CancellationToken      ct = default
    ) {
        return await operations.CancelAsync(request.CanonicalName ?? string.Empty, ct);
    }
}
