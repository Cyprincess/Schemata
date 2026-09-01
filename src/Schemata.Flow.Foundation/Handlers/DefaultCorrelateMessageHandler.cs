using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Flow.Skeleton.Models;
using Schemata.Messaging.Skeleton;
using CorrelateProcessRequest = Schemata.Flow.Foundation.Commands.CorrelateMessageRequest;

namespace Schemata.Flow.Foundation.Handlers;

internal sealed class DefaultCorrelateMessageHandler(FlowHandlerSupport support)
    : IRequestHandler<CorrelateProcessRequest, ProcessSnapshot>
{
    public async Task<ProcessSnapshot> HandleAsync(CorrelateProcessRequest request, CancellationToken ct = default) {
        var process      = await support.LoadProcessAsync(request.ProcessCanonicalName, ct);
        var registration = support.ResolveRegistration(process.DefinitionName);
        var message = registration.Definition.Messages.FirstOrDefault(current => current.Name == request.MessageName);
        if (message is null) {
            throw new InvalidArgumentException(
                SchemataResources.PROCESS_MESSAGE_NOT_DEFINED,
                new Dictionary<string, string?> { ["name"] = request.MessageName }
            );
        }

        var payload = FlowHandlerSupport.DeserializePayload(
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
            ct);
    }
}
