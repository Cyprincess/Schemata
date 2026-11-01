using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Schemata.Authorization.Skeleton.Json;

/// <summary>
///     Serializes a protocol <see langword="long" /> member as a JSON number, overriding the
///     shared ambient long-to-string coercion for fields whose owning protocol defines numeric
///     semantics — introspection <c>exp</c>/<c>iat</c>/<c>nbf</c>/<c>auth_time</c> per
/// <seealso href="https://www.rfc-editor.org/rfc/rfc7662.html#section-2.2">RFC 7662 §2.2</seealso>
/// and registration <c>client_id_issued_at</c>/<c>client_secret_expires_at</c> per
/// <seealso href="https://openid.net/specs/openid-connect-registration-1_0.html">OIDC Dynamic Client Registration</seealso>.
///     Deserialization accepts both numbers and strings, so a client that mirrors the field back
///     as either token round-trips. JWT NumericDates stay JOSE-encoded numbers; resource
///     endpoints keep the global long-to-string precision convention.
/// </summary>
public sealed class ProtocolNumberConverter : JsonConverter<long>
{
    /// <summary>Shared instance; stateless.</summary>
    public static ProtocolNumberConverter Instance { get; } = new();

    public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
        return reader.TokenType switch {
            JsonTokenType.Number => reader.GetInt64(),
            JsonTokenType.String when long.TryParse(reader.GetString(), out var value) => value,
            var _ => throw new JsonException(
                $"The JSON value for a protocol numeric field could not be converted to {typeToConvert}: token {reader.TokenType}."),
        };
    }

    public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options) {
        writer.WriteNumberValue(value);
    }
}
