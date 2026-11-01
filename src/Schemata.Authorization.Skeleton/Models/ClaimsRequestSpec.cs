using System;
using System.Collections.Generic;
using System.Linq;
namespace Schemata.Authorization.Skeleton.Models;

/// <summary>
///     Per-claim request specification members of the <c>claims</c> parameter
///     (OpenID Connect Core 1.0 §5.5.1): <c>essential</c>, <c>value</c>, and <c>values</c>.
/// </summary>
public sealed class ClaimsRequestSpec
{
    /// <summary>
    ///     Whether the claim is an Essential Claim; <see langword="null" /> means the default
    ///     (voluntary) request.
    /// </summary>
    public bool? Essential { get; set; }

    /// <summary>Requests the claim be returned with this exact value (equality comparison).</summary>
    public string? Value { get; set; }

    /// <summary>Requests the claim be returned with one of these values, in preference order.</summary>
    public List<string>? Values { get; set; }

    /// <summary>
    ///     Whether an actual claim value satisfies the <see cref="Value" /> / <see cref="Values" />
    ///     qualifier (OpenID Connect Core 1.0 §5.5.1: equality comparison; a request without a
    ///     qualifier is satisfied by any value).
    /// </summary>
    public bool Matches(string value) {
        if (Value is not null) {
            return string.Equals(Value, value, StringComparison.Ordinal);
        }

        if (Values is not null) {
            return Values.Contains(value, StringComparer.Ordinal);
        }

        return true;
    }
}