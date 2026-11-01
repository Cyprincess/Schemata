using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Resource;
using Schemata.Flow.Skeleton.Models;
using Schemata.Messaging.Skeleton;
using ThrowProcessSignalRequest = Schemata.Flow.Foundation.Commands.ThrowSignalRequest;
using Schemata.Abstractions.Advisors;

namespace Schemata.Flow.Foundation;

/// <summary>
///     Handles BPMN signal broadcast requests dispatched through the resource-method pipeline.
/// </summary>
public sealed class ThrowSignalHandler(IRequestDispatcher dispatcher)
    : IRequestHandler<ThrowSignalRequest, EmptyResourceResponse>
{
    public async Task<EmptyResourceResponse> HandleAsync(
        ThrowSignalRequest request,
        AdviceContext context,
        CancellationToken ct = default)
    {
        await dispatcher.SendAsync<ThrowProcessSignalRequest, IReadOnlyList<SignalDeliveryResult>>(
            new(request.SignalName, request.Payload, request.Token, request.Principal), ct);

        // The broadcast completed: report the commit so the idempotency wrap can finalize the
        // reservation. A failure throws past this point and the outcome stays unreported.
        request.Dispatch?.MarkCommitted();
        return new();
    }
}
