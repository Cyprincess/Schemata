using System;
using System.Collections.Generic;
using Schemata.Authorization.Skeleton;

namespace Schemata.Authorization.Foundation.Authentication;

/// <summary>
///     Canonical configuration for the DPoP flow feature, per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc9449.html">RFC 9449: OAuth 2.0 Demonstrating Proof of Possession (DPoP)</seealso>
///     . Registered by <c>AddSchemataAuthorization()</c> so every consumer resolves
///     <c>IOptions&lt;DPopOptions&gt;</c> whether or not the feature is installed; the callback
///     of <c>UseDemonstratingProofOfPossession()</c> customizes it. Every member carries its
///     RFC-recommended default, so an untouched registration leaves server behavior unchanged.
/// </summary>
public sealed class DPopOptions
{
    /// <summary>
    ///     Whether every client is treated as registered with
    ///     <c>dpop_bound_access_tokens</c>, making the §5.2 rejection of proof-less token
    ///     requests apply host-wide. Off by default: per
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc9449.html#section-5.2">
    ///         RFC 9449: OAuth 2.0 Demonstrating Proof-of-Possession at the Application Layer
    ///         (DPoP) §5.2: Client Registration Metadata
    ///     </seealso>
    ///     the requirement is a per-client registration concern.
    /// </summary>
    public bool RequireAllClients { get; private set; }

    /// <summary>
    ///     Acceptable window around the current time for the <c>iat</c> claim of a DPoP proof,
    ///     bounding both its age and how far in the future it may be minted,
    ///     per
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc9449.html#section-4.3">
    ///         RFC 9449: OAuth 2.0 Demonstrating Proof-of-Possession at the Application Layer
    ///         (DPoP) §4.3: Checking DPoP Proofs, step 11
    ///     </seealso>
    ///     and
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc9449.html#section-11.1">
    ///         §11.1: DPoP Proof Replay
    ///     </seealso>
    ///     . The RFC requires a window "on the order of seconds or minutes" without
    ///     fixing a value; the default follows that guidance with thirty seconds.
    /// </summary>
    public TimeSpan ProofTimeWindow { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     Lifetime of a stored authorization-server DPoP nonce: the window in which a
    ///     client may echo it back in a DPoP proof, per
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc9449.html#section-8">
    ///         RFC 9449: OAuth 2.0 Demonstrating Proof-of-Possession at the Application Layer
    ///         (DPoP) §8: Authorization Server-Provided Nonce
    ///     </seealso>
    ///     .
    /// </summary>
    public TimeSpan NonceLifetime { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    ///     JWS asymmetric signature algorithms accepted for DPoP proofs,
    ///     per
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc9449.html#section-4.3">
    ///         RFC 9449: OAuth 2.0 Demonstrating Proof-of-Possession at the Application Layer
    ///         (DPoP) §4.3: Checking DPoP Proofs, step 5
    ///     </seealso>
    ///     . Defaults to the nine RFC 7518 recommended asymmetric algorithms;
    ///     <c>none</c> and symmetric (MAC) algorithms are always rejected.
    /// </summary>
    public HashSet<string> SigningAlgorithms { get; } = new(StringComparer.Ordinal) {
        AuthorizationConstants.SigningAlgorithms.RsaSha256,
        AuthorizationConstants.SigningAlgorithms.RsaSha384,
        AuthorizationConstants.SigningAlgorithms.RsaSha512,
        AuthorizationConstants.SigningAlgorithms.RsaPssSha256,
        AuthorizationConstants.SigningAlgorithms.RsaPssSha384,
        AuthorizationConstants.SigningAlgorithms.RsaPssSha512,
        AuthorizationConstants.SigningAlgorithms.EcdsaSha256,
        AuthorizationConstants.SigningAlgorithms.EcdsaSha384,
        AuthorizationConstants.SigningAlgorithms.EcdsaSha512,
    };

    /// <summary>
    ///     Requires a DPoP proof on every token request from every client, applying the
    ///     §5.2 <c>dpop_bound_access_tokens</c> policy without editing client
    ///     registrations.
    /// </summary>
    /// <returns>The current options for chaining.</returns>
    public DPopOptions RequireForAllClients() {
        RequireAllClients = true;
        return this;
    }
}
