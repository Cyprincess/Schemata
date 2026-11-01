using System;
using System.Collections.Generic;
using System.Text;

namespace Schemata.Transport.RabbitMq;

/// <summary>Encodes propagated execution items in AMQP headers.</summary>
public static class MessageContextHeaders
{
    /// <summary>Prefix isolating propagated context from any other header a deployment adds.</summary>
    public const string Prefix = "schemata-ctx-";

    /// <summary>Encodes propagated items into AMQP headers.</summary>
    public static IDictionary<string, object?>? Write(IReadOnlyDictionary<string, string?>? items) {
        if (items is null || items.Count == 0) {
            return null;
        }

        var headers = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var (key, value) in items) {
            headers[Prefix + key] = value is null ? null : Encoding.UTF8.GetBytes(value);
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
