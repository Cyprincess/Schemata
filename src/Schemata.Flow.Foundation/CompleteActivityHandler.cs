using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Flow.Skeleton.Models;
using Schemata.Messaging.Skeleton;
using CompleteProcessRequest = Schemata.Flow.Foundation.Commands.CompleteActivityRequest;

namespace Schemata.Flow.Foundation;

/// <summary>
///     Handles process-instance activity-completion requests dispatched through the resource-method pipeline.
/// </summary>
public sealed class CompleteActivityHandler(IRequestDispatcher dispatcher)
    : IRequestHandler<CompleteActivityRequest, ProcessSnapshot>
{
    public async Task<ProcessSnapshot> HandleAsync(
        CompleteActivityRequest request,
        CancellationToken ct = default)
    {
        var canonicalName = request.CanonicalName
            ?? throw new InvalidOperationException("Instance method requires a target canonical name.");
        return await dispatcher.SendAsync<CompleteProcessRequest, ProcessSnapshot>(new(
            canonicalName, request.Token, request.Principal), ct);
    }
}
