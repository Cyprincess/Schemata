using System;
using System.Text.Json;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Common;

namespace Schemata.Flow.Foundation.Handlers;

/// <summary>Converts transport payloads into the shapes Flow task bodies and triggers consume.</summary>
internal static class FlowPayload
{
    internal static object? Deserialize(object? payload, Type? type) {
        if (payload is Typed typedPayload) {
            return typedPayload.Value;
        }

        if (payload is not string text) {
            return payload;
        }

        if (string.IsNullOrEmpty(text)) {
            return null;
        }

        if (type is null) {
            throw new InvalidArgumentException(SchemataResources.INVALID_PAYLOAD);
        }

        return JsonSerializer.Deserialize(text, type, SchemataJson.Default);
    }

    internal static object? Preserve(object? payload) {
        return payload is string ? new Typed(payload) : payload;
    }

    internal sealed record Typed(object Value);
}
