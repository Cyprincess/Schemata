using System;

namespace Schemata.Authorization.Foundation.Services;

/// <summary>
///     Bounded conversion of a JWT NumericDate (seconds since the Unix epoch). Client-supplied
///     values outside the <see cref="DateTimeOffset" /> range report failure so the caller can
///     reject them through its protocol error contract instead of surfacing
///     <see cref="ArgumentOutOfRangeException" />.
/// </summary>
internal static class NumericDates
{
    private static readonly long Min =
        (DateTimeOffset.MinValue - DateTimeOffset.UnixEpoch).Ticks / TimeSpan.TicksPerSecond;

    private static readonly long Max =
        (DateTimeOffset.MaxValue - DateTimeOffset.UnixEpoch).Ticks / TimeSpan.TicksPerSecond;

    public static bool TryFromUnixTimeSeconds(long seconds, out DateTimeOffset value) {
        if (seconds < Min || seconds > Max) {
            value = default;
            return false;
        }

        value = DateTimeOffset.FromUnixTimeSeconds(seconds);
        return true;
    }
}
