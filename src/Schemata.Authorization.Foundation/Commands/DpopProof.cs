using System;
using System.Collections.Generic;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Commands;

/// <summary>AdviceContext carrier for the DPoP proof of a token request.</summary>
/// <param name="Value">Raw DPoP proof JWT header value, or <see langword="null" /> when absent.</param>
public sealed record DpopProof(string? Value)
{
    /// <summary>
    ///     Extracts the DPoP proof from a request header map, per RFC 9449 §4.3 step 1: at most
    ///     one DPoP HTTP header field, carrying one raw value. Field names match
    ///     case-insensitively; raw value multiplicity is preserved end to end.
    /// </summary>
    /// <param name="headers">Request headers keyed by field name, or <see langword="null" />.</param>
    /// <returns>A carrier whose <see cref="Value" /> is <see langword="null" /> when the header is absent.</returns>
    /// <exception cref="OAuthException">
    ///     <see cref="OAuthErrors.InvalidDpopProof" /> when the field carries several, empty, or
    ///     comma-combined values.
    /// </exception>
    public static DpopProof FromHeaders(IReadOnlyDictionary<string, List<string?>>? headers) {
        if (headers is null) {
            return new((string?)null);
        }

        string? proof = null;
        var     found = false;
        foreach (var (name, values) in headers) {
            if (!string.Equals(name, Headers.Dpop, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            foreach (var value in values) {
                if (found) {
                    throw Malformed();
                }

                found = true;
                proof = value;
            }
        }

        if (!found) {
            return new((string?)null);
        }

        // A compact JWT never contains a comma; one marks a comma-joined repetition of the field.
        if (string.IsNullOrWhiteSpace(proof) || proof.Contains(',')) {
            throw Malformed();
        }

        return new(proof);

        static OAuthException Malformed() {
            return new(OAuthErrors.InvalidDpopProof, SchemataResources.DPOP_PROOF_MALFORMED);
        }
    }
}
