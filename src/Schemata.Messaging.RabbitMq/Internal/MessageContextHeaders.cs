using System;
using System.Collections.Generic;
using System.Text;
using Schemata.Messaging.Skeleton;

namespace Schemata.Messaging.RabbitMq.Internal;

/// <summary>
///     Codec between <see cref="MessageContext" /> and AMQP headers.
/// </summary>
/// <remarks>
///     Split out from the dispatcher so the round trip is testable without a broker: AMQP delivers
///     header values as <see cref="byte" /> arrays, which is the one detail most likely to be got
///     wrong and the one a broker-less test can still prove.
/// </remarks>
internal static class MessageContextHeaders
{
    /// <summary>Prefix isolating propagated context from any other header a deployment adds.</summary>
    internal const string Prefix = "schemata-ctx-";

    /// <summary>Encodes <paramref name="context" /> into AMQP headers.</summary>
    /// <remarks>An empty context produces no headers at all, so an unconfigured deployment pays nothing.</remarks>
    public static IDictionary<string, object?>? Write(MessageContext? context) {
        if (context is null || context.Items.Count == 0) {
            return null;
        }

        var headers = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var (key, value) in context.Items) {
            // A null item is carried as an absent header rather than an empty string, so the far
            // side can tell "not set" from "set to empty".
            if (value is not null) {
                headers[Prefix + key] = Encoding.UTF8.GetBytes(value);
            }
        }

        return headers.Count == 0 ? null : headers;
    }

    /// <summary>Decodes the propagated items out of AMQP <paramref name="headers" />.</summary>
    public static IReadOnlyDictionary<string, string?> Read(IDictionary<string, object?>? headers) {
        if (headers is null || headers.Count == 0) {
            return new Dictionary<string, string?>();
        }

        var items = new Dictionary<string, string?>(StringComparer.Ordinal);

        foreach (var (key, value) in headers) {
            if (!key.StartsWith(Prefix, StringComparison.Ordinal)) {
                continue;
            }

            items[key[Prefix.Length..]] = value switch {
                byte[] bytes => Encoding.UTF8.GetString(bytes),
                string text  => text,
                _            => value?.ToString(),
            };
        }

        return items;
    }
}
