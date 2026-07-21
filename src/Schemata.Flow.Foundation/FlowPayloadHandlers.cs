using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Foundation;

internal sealed class FlowCorrelateMessageHandler(FlowRunner runner, IProcessRegistry registry)
    : IResourceMethodHandler<SchemataProcess, CorrelateMessageRequest, ProcessSnapshot>
{
    public ValueTask<ProcessSnapshot> InvokeAsync(
        string?                 name,
        CorrelateMessageRequest request,
        SchemataProcess?        entity,
        ClaimsPrincipal?        principal,
        CancellationToken       ct
    ) {
        ArgumentNullException.ThrowIfNull(entity);
        var reg = registry.GetRegistration(entity.DefinitionName);
        if (reg is null) {
            throw new NotFoundException(
                SchemataResources.PROCESS_NOT_REGISTERED,
                new Dictionary<string, string?> { ["name"] = entity.DefinitionName }
            );
        }

        var payload = FlowPayload.Deserialize(request.Payload, reg.MessagePayloadTypes.GetValueOrDefault(request.MessageName));
        return runner.CorrelateAsync(entity, request.MessageName, payload, request.Token, principal, ct);
    }
}

internal sealed class FlowThrowSignalHandler(FlowRunner runner, IProcessRegistry registry)
    : IResourceMethodHandler<SchemataProcess, ThrowSignalRequest, EmptyResourceResponse>
{
    public async ValueTask<EmptyResourceResponse> InvokeAsync(
        string?            name,
        ThrowSignalRequest request,
        SchemataProcess?   entity,
        ClaimsPrincipal?   principal,
        CancellationToken  ct
    ) {
        var payload = FlowPayload.Deserialize(request.Payload, SignalPayloadType(registry, request.SignalName));
        await runner.ThrowSignalAsync(request.SignalName, payload, request.Token, principal, ct);
        return new();
    }

    private static Type? SignalPayloadType(IProcessRegistry registry, string signalName) {
        var types = new HashSet<Type>();
        foreach (var process in registry.GetRegisteredProcesses()) {
            var reg = registry.GetRegistration(process);
            if (reg?.Definition.Signals.Any(s => s.Name == signalName) is not true) {
                continue;
            }

            if (reg.SignalPayloadTypes.TryGetValue(signalName, out var type)) {
                types.Add(type);
            }
        }

        if (types.Count > 1) {
            throw new InvalidArgumentException(SchemataResources.INVALID_PAYLOAD);
        }

        return types.FirstOrDefault();
    }
}

internal static class FlowPayload
{
    /// <summary>Deserializes an embedded message/signal payload using the framework's shared internal options.</summary>
    /// <remarks>
    ///     Embedded payloads are bound by CLR property name with case-insensitive matching via
    ///     <see cref="SchemataJson.Default" />; they deliberately do NOT follow the HTTP snake_case
    ///     wire policy configured by the transport JSON feature.
    /// </remarks>
    public static object? Deserialize(string? payload, Type? type) {
        if (string.IsNullOrEmpty(payload)) {
            return null;
        }

        if (type is null) {
            throw new InvalidArgumentException(SchemataResources.INVALID_PAYLOAD);
        }

        return JsonSerializer.Deserialize(payload, type, SchemataJson.Default);
    }
}
