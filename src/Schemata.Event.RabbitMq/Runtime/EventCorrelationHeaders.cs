using System;
using System.Collections.Generic;
using System.Text;

namespace Schemata.Event.RabbitMq.Runtime;

/// <summary>Encodes business correlation metadata in AMQP headers.</summary>
public static class EventCorrelationHeaders
{
    /// <summary>Prefix isolating correlation entries from propagated context headers.</summary>
    public const string Prefix = "schemata-cor-";

    /// <summary>Writes correlation entries into <paramref name="headers" />.</summary>
    public static void Write(IDictionary<string, object?> headers, IReadOnlyDictionary<string, string>? correlation) {
        if (correlation is null || correlation.Count == 0) {
            return;
        }

        foreach (var (key, value) in correlation) {
            headers[Prefix + key] = Encoding.UTF8.GetBytes(value);
        }
    }

    /// <summary>Decodes correlation entries out of AMQP <paramref name="headers" />; null when absent.</summary>
    public static IReadOnlyDictionary<string, string>? Read(IDictionary<string, object?>? headers) {
        if (headers is null || headers.Count == 0) {
            return null;
        }

        Dictionary<string, string>? correlation = null;

        foreach (var (key, value) in headers) {
            if (!key.StartsWith(Prefix, StringComparison.Ordinal)) {
                continue;
            }

            correlation ??= new(StringComparer.Ordinal);
            correlation[key[Prefix.Length..]] = value switch {
                byte[] bytes => Encoding.UTF8.GetString(bytes),
                string text  => text,
                _            => value?.ToString() ?? string.Empty,
            };
        }

        return correlation;
    }
}
