using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Messaging.Skeleton;
using TerminateProcessCommand = Schemata.Flow.Foundation.Commands.TerminateProcessRequest;

namespace Schemata.Flow.Foundation;

/// <summary>
///     Handles process-instance termination requests dispatched through the resource-method pipeline.
/// </summary>
public sealed class TerminateProcessHandler(IRequestDispatcher dispatcher)
    : IRequestHandler<TerminateProcessResourceRequest, ProcessSnapshot>
{
    /// <inheritdoc />
    public async Task<ProcessSnapshot> HandleAsync(
        TerminateProcessResourceRequest request,
        CancellationToken ct = default)
    {
        var canonicalName = request.CanonicalName
            ?? throw new InvalidOperationException("Instance method requires a target canonical name.");
        return await dispatcher.SendAsync<TerminateProcessCommand, ProcessSnapshot>(new(canonicalName, request.Principal), ct);
    }
}
