using System.Collections.Generic;
using System.Text;

namespace Schemata.Security.Foundation.Signatures;

/// <summary>
///     Percent-decoding and percent-encoding for <c>@query-param</c> per the
///     <c>application/x-www-form-urlencoded</c> parser and serializer referenced by RFC 9421
///     section 2.2.8: decoding maps <c>+</c> to space and percent-decodes UTF-8; encoding emits
///     UTF-8 with every byte outside the unreserved set (<c>A–Z a–z 0–9 * - . _</c>)
///     percent-encoded with uppercase hex — space as <c>%20</c>, matching the section 2.2.8
///     examples.
/// </summary>
internal static class FormUrlEncoded
{
    public static string Decode(string value) {
        if (value.IndexOfAny(['%', '+']) < 0) {
            return value;
        }

        var bytes  = new List<byte>(value.Length);
        for (var index = 0; index < value.Length; index++) {
            var character = value[index];
            if (character == '+') {
                bytes.Add((byte)' ');
            } else if (character == '%' && index + 2 <= value.Length - 1 && TryParseHex(value, index, out var decoded)) {
                bytes.Add(decoded);
                index += 2;
            } else {
                bytes.AddRange(Encoding.UTF8.GetBytes(character.ToString()));
            }
        }

        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    private static bool TryParseHex(string value, int index, out byte decoded) {
        var high = FromHex(value[index + 1]);
        var low  = FromHex(value[index + 2]);
        if (high < 0 || low < 0) {
            decoded = 0;
            return false;
        }

        decoded = (byte)((high << 4) | low);
        return true;
    }

    private static int FromHex(char character) {
        return character switch {
            >= '0' and <= '9' => character - '0',
            >= 'a' and <= 'f' => character - 'a' + 10,
            >= 'A' and <= 'F' => character - 'A' + 10,
            _                 => -1,
        };
    }

    public static string Encode(string value) {
        var builder = new StringBuilder(value.Length);
        foreach (var current in Encoding.UTF8.GetBytes(value)) {
            if (IsUnreserved(current)) {
                builder.Append((char)current);
            } else {
                builder.Append('%').Append(current.ToString("X2"));
            }
        }

        return builder.ToString();
    }

    private static bool IsUnreserved(byte value) {
        return value is >= (byte)'A' and <= (byte)'Z'
            or >= (byte)'a' and <= (byte)'z'
            or >= (byte)'0' and <= (byte)'9'
            or (byte)'*' or (byte)'-' or (byte)'.' or (byte)'_';
    }
}
