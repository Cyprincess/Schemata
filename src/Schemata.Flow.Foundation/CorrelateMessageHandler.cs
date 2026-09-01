using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Flow.Skeleton.Models;
using Schemata.Messaging.Skeleton;
using CorrelateProcessRequest = Schemata.Flow.Foundation.Commands.CorrelateMessageRequest;

namespace Schemata.Flow.Foundation;

/// <summary>
///     Handles process-instance message-correlation requests dispatched through the resource-method pipeline.
/// </summary>
public sealed class CorrelateMessageHandler(IRequestDispatcher dispatcher)
    : IRequestHandler<CorrelateMessageRequest, ProcessSnapshot>
{
    public async Task<ProcessSnapshot> HandleAsync(
        CorrelateMessageRequest request,
        CancellationToken ct = default)
    {
        var canonicalName = request.CanonicalName
            ?? throw new InvalidOperationException("Instance method requires a target canonical name.");
        return await dispatcher.SendAsync<CorrelateProcessRequest, ProcessSnapshot>(new(
            canonicalName,
            request.MessageName,
            request.Payload,
            request.Token,
            request.Principal), ct);
    }
}
