using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Messaging.Skeleton;
using CorrelateProcessRequest = Schemata.Flow.Foundation.Commands.CorrelateMessageRequest;
using Schemata.Abstractions.Advisors;

namespace Schemata.Flow.Foundation.Handlers;

internal sealed class DefaultCorrelateMessageHandler<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource>(
    FlowHandlerSupport<TProcess, TToken, TTransition, TCompensation, TParticipant, TSource> support
)
    : IRequestHandler<CorrelateProcessRequest, ProcessSnapshot>
    where TProcess : SchemataProcess, new()
    where TToken : SchemataProcessToken, new()
    where TTransition : SchemataProcessTransition, new()
    where TCompensation : SchemataProcessCompensation, new()
    where TParticipant : SchemataProcessParticipant, new()
    where TSource : SchemataProcessSource, new()
{
    public async Task<ProcessSnapshot> HandleAsync(CorrelateProcessRequest request, AdviceContext context, CancellationToken ct = default) {
        var process      = await support.LoadProcessAsync(request.ProcessCanonicalName, ct);
        var registration = support.ResolveRegistration(process);
        var message = registration.Definition.Messages.FirstOrDefault(current => current.Name == request.MessageName);
        if (message is null) {
            throw new InvalidArgumentException(
                SchemataResources.PROCESS_MESSAGE_NOT_DEFINED,
                new Dictionary<string, string?> { ["name"] = request.MessageName }
            );
        }

        var payload = FlowPayload.Deserialize(
            request.Payload,
            registration.MessagePayloadTypes.GetValueOrDefault(request.MessageName));
        return await support.TriggerAddressedAsync(
            process,
            registration,
            support.ResolveEngine(registration),
            message,
            payload,
            request.Token,
            resolveTarget: true,
            request.Principal,
            context,
            ct);
    }
}
