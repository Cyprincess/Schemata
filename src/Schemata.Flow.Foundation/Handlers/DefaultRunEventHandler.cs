using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Flow.Foundation.Commands;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Messaging.Skeleton;
using Schemata.Abstractions.Advisors;

namespace Schemata.Flow.Foundation.Handlers;

internal sealed class DefaultRunEventHandler<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource>(
    FlowHandlerSupport<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource> support
)
    : IRequestHandler<RunEventRequest, ProcessSnapshot>
    where TProcess : SchemataProcess, new()
    where TToken : SchemataProcessToken, new()
    where TTransition : SchemataProcessTransition, new()
    where TCompensation : SchemataProcessCompensation, new()
    where TParticipant : SchemataProcessParticipant, new()
    where TSource : SchemataProcessSource, new()
{
    public async Task<ProcessSnapshot> HandleAsync(RunEventRequest request, AdviceContext context, CancellationToken ct = default) {
        ArgumentNullException.ThrowIfNull(request.Trigger);

        var process      = await support.LoadProcessAsync(request.ProcessCanonicalName, ct);
        var registration = support.ResolveRegistration(process);
        return await support.TriggerAddressedAsync(
            process,
            registration,
            support.ResolveEngine(registration),
            request.Trigger,
            request.Payload,
            request.Token,
            resolveTarget: false,
            principal: request.Principal,
            context,
            ct);
    }
}
