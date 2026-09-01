using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Flow.Foundation.Commands;
using Schemata.Flow.Skeleton.Models;
using Schemata.Messaging.Skeleton;

namespace Schemata.Flow.Foundation.Handlers;

internal sealed class DefaultRunEventHandler(FlowHandlerSupport support)
    : IRequestHandler<RunEventRequest, ProcessSnapshot>
{
    public async Task<ProcessSnapshot> HandleAsync(RunEventRequest request, CancellationToken ct = default) {
        ArgumentNullException.ThrowIfNull(request.Trigger);

        var process      = await support.LoadProcessAsync(request.ProcessCanonicalName, ct);
        var registration = support.ResolveRegistration(process.DefinitionName);
        return await support.TriggerAddressedAsync(
            process,
            registration,
            support.ResolveEngine(registration),
            request.Trigger,
            request.Payload,
            request.Token,
            resolveTarget: false,
            principal: null,
            ct);
    }
}
