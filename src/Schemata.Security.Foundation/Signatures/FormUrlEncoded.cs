using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Schemata.Security.Foundation.Signatures;

/// <summary>
///     Percent-decoding and percent-encoding for <c>@query-param</c> per the
///     <c>application/x-www-form-urlencoded</c> parser and serializer referenced by RFC 9421
///     section 2.2.8: decoding maps <c>+</c> to space and percent-decodes UTF-8; encoding emits
///     UTF-8 with every byte outside the unreserved set (<c>A–Z a–z 0–9 * - . _</c>)
///     percent-encoded with uppercase hex — space as <c>%20</c>, matching the section 2.2.8
///     examples. Input that cannot be preserved as UTF-8 fails; neither direction degrades
///     into replacement bytes.
/// </summary>
internal static class FormUrlEncoded
{
    // A lone surrogate in the input or an escaped sequence that is not UTF-8 cannot be
    // preserved; strict encoding reports the failure instead of substituting EF BF BD.
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static bool TryDecode(string value, [NotNullWhen(true)] out string? decoded) {
        if (value.IndexOfAny(['%', '+']) < 0) {
            decoded = value;
            return true;
        }

        var bytes = new List<byte>(value.Length);
        var start = 0;
        for (var index = 0; index < value.Length; index++) {
            var character = value[index];
            if (character == '+') {
                if (!TryAddSpan(value, start, index, bytes)) {
                    decoded = null;
                    return false;
                }

                bytes.Add((byte)' ');
                start = index + 1;
            } else if (character == '%' && index + 2 <= value.Length - 1 && TryParseHex(value, index, out var octet)) {
                if (!TryAddSpan(value, start, index, bytes)) {
                    decoded = null;
                    return false;
                }

                bytes.Add(octet);
                index += 2;
                start = index + 1;
            }
        }

        if (!TryAddSpan(value, start, value.Length, bytes)) {
            decoded = null;
            return false;
        }

        try {
            decoded = StrictUtf8.GetString(bytes.ToArray());
            return true;
        } catch (DecoderFallbackException) {
            decoded = null;
            return false;
        }
    }

    // Literal characters are UTF-8-encoded as one contiguous span so a surrogate pair stays
    // whole; a span with no UTF-8 representation fails the decode.
    private static bool TryAddSpan(string value, int start, int end, List<byte> bytes) {
        if (end <= start) {
            return true;
        }

        try {
            bytes.AddRange(StrictUtf8.GetBytes(value.Substring(start, end - start)));
            return true;
        } catch (EncoderFallbackException) {
            return false;
        }
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

    public static bool TryEncode(string value, [NotNullWhen(true)] out string? encoded) {
        byte[] octets;
        try {
            octets = StrictUtf8.GetBytes(value);
        } catch (EncoderFallbackException) {
            encoded = null;
            return false;
        }

        var builder = new StringBuilder(octets.Length);
        foreach (var current in octets) {
            if (IsUnreserved(current)) {
                builder.Append((char)current);
            } else {
                builder.Append('%').Append(current.ToString("X2"));
            }
        }

        encoded = builder.ToString();
        return true;
    }

    private static bool IsUnreserved(byte value) {
        return value is >= (byte)'A' and <= (byte)'Z'
            or >= (byte)'a' and <= (byte)'z'
            or >= (byte)'0' and <= (byte)'9'
            or (byte)'*' or (byte)'-' or (byte)'.' or (byte)'_';
    }
}
