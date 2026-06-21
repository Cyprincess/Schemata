using System;

namespace Schemata.Insight.Foundation;

/// <summary>
///     Encodes the next-page skip offset as an opaque token. This phase uses plain offset paging;
///     token signing and query binding are deferred.
/// </summary>
internal static class InsightPageToken
{
    public static string Encode(int skip) {
        return Convert.ToBase64String(BitConverter.GetBytes(skip));
    }

    public static int Decode(string token) {
        try {
            var bytes = Convert.FromBase64String(token);
            if (bytes.Length == sizeof(int)) {
                return BitConverter.ToInt32(bytes);
            }
        } catch (FormatException) {
            // Falls through to the validation error below.
        }

        throw new InsightValidationException(InsightReasons.InvalidArgument, "Invalid page token.");
    }
}
