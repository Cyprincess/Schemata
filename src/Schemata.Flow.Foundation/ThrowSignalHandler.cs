using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Messaging.Skeleton;
using ThrowProcessSignalRequest = Schemata.Flow.Foundation.Commands.ThrowSignalRequest;

namespace Schemata.Flow.Foundation;

/// <summary>
///     Handles BPMN signal broadcast requests dispatched through the resource-method pipeline.
/// </summary>
public sealed class ThrowSignalHandler(IRequestDispatcher dispatcher)
    : IRequestHandler<ThrowSignalRequest, EmptyResourceResponse>
{
    /// <inheritdoc />
    public async Task<EmptyResourceResponse> HandleAsync(
        ThrowSignalRequest request,
        CancellationToken ct = default)
    {
        await dispatcher.SendAsync<ThrowProcessSignalRequest, IReadOnlyList<SignalDeliveryResult>>(
            new(request.SignalName, request.Payload, request.Token, request.Principal), ct);
        return new();
    }
}
